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
rectangle and yellow anchor cross. That is diagnostic layering, not original
occlusion or placement parity.

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
viewer reads the launcher settings `user://launcher_v1.json` ([research](../research/launcher-settings.md#configuration-file);
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
[research](../research/launcher-settings.md#recommended-shape-option-a) option A, question 2 A) when
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
  after alpha); "Show this screen at every start"; "Debug mode" (ARC-DEV-050, off by default:
  [debug mode](area-map-mode.md#debug-mode-arc-dev-050)); Play, Reset settings and Quit. The project's own look, not a
  window of the original's menu.
- **Play** is offered only for a ready folder. It stores the folder with its source and identity (the start leaves a
  detected folder and a changed installation unwritten, so Play is where they are confirmed), the rule mode, "show
  at start" and the debug mode (`debugMode`, which turns the run's debug mode on, as `--debug-mode` does), then starts map mode on the main map with the main menu from that folder, as a start without the screen
  does. A save the file refuses (a newer build's or an unreadable file) does not stop the start: the choices apply to
  it and the file is kept.
- **Reset settings** writes the defaults over the file (`LauncherSettingsFile.Reset`, the one write that replaces a
  newer build's or an unreadable file); the shown folder stays, for Play to store again.
- **Rule mode.** Every ordinary run, with inputs or without, starts in `rules.active` of `launcher_v1.json`
  (`LauncherSettings.Rules`); the options window's Rules line writes it there through the same store (one owner per
  file), reading the file again and changing only `rules.active`, so a folder another run stored meanwhile is kept. An
  automated run without `LAUNCHER_SETTINGS_TEST_PATH` has no file and starts in Steamwoven. A start that fails after the
  screen (an unexpected error) shows `failure stage=GameFolder code=StartFailed` and a native smoke exits with 1.

Self-test `ARC-UI-014 self-test PASS synthetic launcher` (synthetic folders, a temporary settings file, a stand-in for
the start): the first run without a folder and Play off, a non-game folder named in words, Play storing the chosen
folder with its identity, Original and "show at start", that setting reopening the screen, a stored folder going
straight to the start, a request opening it, the Rules line writing the file, Play never overwriting a newer build's
file and Reset replacing it. Native smoke on the original data: `LAUNCHER_SETTINGS_TEST_PATH=FILE
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

In `crash-site-area` mode, the original left click on an NPC whose effective SAP_DIALOG
attachment names a script talks to it (ARC-SCRIPT-005): within 3 tiles at once (project
diagnostic reach), otherwise after the click-to-move route walks within reach. The modal
conversation panel closes the actor gate; replies are clicked (controller menu navigates and
confirms), keyboard keys stay reserved and are not redirected, and a diagnostic Leave button
closes the dialog. Since ARC-SCRIPT-009 the talk request and the script's continuation after a
"return to script" end run through the [SCR dispatcher](../scripting/scr-dispatcher-runtime.md#dialogue-arc-script-009-slice-s3):
a further dialog action opens the next conversation, anything else is a HUD notice. The HUD says whether a dialog NPC is in reach and what the dialogs left in
the game session (quests, rumors, followers; ARC-SCRIPT-006). Rules, state profile and
limits are in the [conversation runtime](../scripting/dialog-conversation-runtime.md); the
self-test covers a synthetic panel, an off-NPC click, keyboard events and polled levels, and the
palette guard in AREA mode (the positive NPC hit test is an opt-in original-data test).

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
position (in `world-diagnostic`; `crash-site-area` uses `(40,20)` since
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

`crash-site-area` with `--game-root GAME --area-map NAME` (no `--prototypes` or Crash
Site inputs) shows one whole interior of the game folder's module content stack
instead of the pinned exterior; see [map mode](area-map-mode.md). Since ARC-RENDER-024 the main world map opens there
as a window that moves with the actor (`--area-map-sector X,Y` picks where it opens;
[the main world map](area-map-mode.md#the-main-world-map-arc-render-024)).

### Crash Site cave and return (ARC-WORLD-009)

`crash-site-area` takes an optional `--area-cave-mobile PATH`, the cave's
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
  arrival cell (`PlaceActor`); the HUD shows it. Both maps stay loaded, so the
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
next hour at literal host times, holds while the palette and a dialog are open, waits +1 hour during the pause
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
`crash-site-area` the placement now takes the chart's location origin `(40,0)`
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

While `crash-site-area` has a game session (every published area since ARC-SCRIPT-006),
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

In `crash-site-area` the pointer names the object under it and the cursor shows what a
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
  missing description shows nothing. Every NPC is shown as not yet met (its unknown
  description, which for Virgil is a generic one, not his name), which is the new-game state: "met before" is not
  session state yet ([reaction research R6](../research/npc-reaction-reputation-evidence.md)),
  so the name does not change after the first talk (NOT READY). The tooltip sits below
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
  a dialog NPC is *talk* within the 3-tile reach, else *walk to talk*; the cave
  entrance or exit is *use* within use range 2, else *walk to use*; anything else is a
  ground goal: *no route* when the route planner refuses it without a search (the cell or
  the actor outside the area, or no neighbour step entering the cell), else *walk here*;
  since ARC-WORLD-042 a goal more than 32 cells from the actor on an axis is *walk here*
  too (the follower walks it along a corridor), and since ARC-WORLD-072 *no route* under the
  Original rule mode (its far click does not move); the *walk to …* actions of far objects do
  not read the mode yet (hover and use reach are ARC-WORLD-068's). The search itself is not run on hover, so
  a *walk here* goal can still end without a route, or short of it (`ROUTE_PARTIAL`). Since ARC-UI-004 an item on the ground
  is *pick up* within reach 1, else *walk to pick up*, and a dead NPC (since ARC-WORLD-101 also a standing container) is *loot* or *walk to
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
  trap gets no second draw. NOT READY: the critters' coloured rings under and over them (arts 467 and 468 in the reaction
  category's colour, 555 in combat mode), the detect-invisible exception, the wading object's two-part draw (the same blit
  for both parts, so no visible difference).
- **Window.** With the interface bars drawn the object is described in the message window (0x553BE0) and the floating
  tooltip is not shown: an item by its item window (its type icon and its name in its colour, as the hotkey bar's,
  ARC-UI-035); a portal (`Door_Icon` 436 or `IconWindow` 786 by its art), a container (by its description) and scenery (437)
  by their image at (217, 510) and their name, scenery's in its three-line block; a critter by its name, the bars of
  ARC-UI-020 on the third and fourth lines (291 px, the viewer's colours: the bar art 463–466 is not drawn; they follow the
  critter's life while it stays hovered) and its image (a follower's portrait, ARC-UI-024, else by its flags or race as the
  loot window's); a trap's name as a hover text. In a pane mode only the name goes into the pane's text area. A scenery with
  an empty name, a wall and a projectile draw nothing. Leaving the object shows the viewed entry again (Steamwoven's ML7; the
  original keeps its drawing); an entry stored while the object is hovered takes the window, and the object's window comes
  back only when the pointer moves to another object. NOT READY: a portal's and container's lock state and hit points line,
  scenery's hit points, a critter's level or reaction line and the combat panel of 0x554830, the item window's further lines
  (HK22), the PC's own portrait; a portal or container with an empty name draws no window in the viewer (in the original
  only scenery checks its name, the others draw their image and lines); a trap's hover text stays after the pointer leaves
  it until the quiet time's next entry (the original's clearing of it there was not read).
- **The plane wreck at the Crash Site** is one scenery object named "Something Big" among 16 wreckage pieces that carry
  `OF_CLICK_THROUGH`. The original's pick skips them (OH1), so only that one part is hovered, highlighted and named: this is
  the original's behaviour, not a Steamwoven bug (OH8).

Checks: `AreaHoverWindowTests` (the dispatch by type, the highlighted types and flags, the images, the window's layout and
its show and leave), `AreaMessageLogTests`; the native smoke stage `hover` (`./arcanum smoke pr --stages hover --hero
human-male`, marker `ARC-UI-057 native-smoke hover-window PASS`: with the bars drawn it looks for an item, a container, a
portal and a scenery on screen (on the start map the first, second and fourth are found; at least one kind must be) and
hovers each found one: the highlight is the body's frame drawn additive, none of its picked pixels gets darker and some
get brighter, the window shows the expected kind, image and name without the tooltip, leaving takes both away;
`ARC_AREA_HOVER_WINDOW_CAPTURE` saves readbacks) and the stage `life` (`ARC-UI-057 native-smoke hover-window critter PASS`: a
follower's and another NPC's window with their name, image or portrait and bars). Not verified: the exact pixel of the
original's 16-bit additive blit (its pixel loop is not read), a portal's window on screen (the start map has none).

### Original cursor art (ARC-UI-056)

In `crash-site-area` (the exterior, the cave and `--area-map`) the pointer is the original's cursor art, picked as the 1.0.7.4
executable picks it ([ledger CU1–CU6](../research/ledger/ui-audio.md#cursor-art-arc-ui-056), FR-UI-005). It follows the
game's state, never the object under the pointer: what a click would do there stays in the tooltip (ARC-UI-005).

- **Which art** (`AreaCursorArt.Art`, CU1–CU3). The spell's targeting cursor open: `spellcur.art` (21). A window over the game
  (inventory, sheet, logbook, schematics, barter, reading, world map, save list, main menu, palette, dialog, bug report, the
  death cover): the gauntlet `cursor.art` (0). In the game out of combat mode: the gauntlet; in combat mode: `battlecur.art`
  (353), or with a called-shot key held (`,` first, then `.`, then `/`, the physical keys, as the attack reads them, FA4) its
  called-shot cursor (818 head, 819 leg picture, 820 arm picture). `skillcur.art` (352) belongs to the skill cursor, which the
  viewer does not have yet.
- **Rule modes** (CUA1). The original shows the leg picture for `.`, which aims at the arm, and the arm picture for `/`,
  which aims at the leg. Original keeps that; Steamwoven (default) shows the aimed location's picture
  (`InterfaceRuleProfile.CalledShotCursorsSwapped`, [rule modes](../world/rule-modes.md)). A mode switch shows at the next frame.
- **Drawing** (CU4, CU5). Each art's frames are read once a run from the game folder of the area's module (as the interface
  bars' art) and shown at their own pixel size as Godot's custom arrow cursor with the frame's hot point (the ART frame
  header's centre) on the pointer. `spellcur.art` steps its 7 frames every 100 ms (10 fps) from the moment the art was set,
  wrapping; the others have one frame. The same cursor serves the world, the bars and the windows, as in the original;
  controls with a cursor shape of their own (a text field's I-beam) keep it. The log prints `ARC-UI-056 cursor art ready=…`
  once and `ARC-UI-056 cursor art=N mode=M` at each change.
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

Slice 3 of the [combat HUD plan](../research/combat-hud.md#slices) (FR-UI-002), in `crash-site-area` with plain Godot
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
  the portrait, as the original does. A follower's values are its combat values on the shown map (a visiting companion's on its home map); a follower
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

### Floating text (ARC-UI-021)

Slice 4 of the [combat HUD plan](../research/combat-hud.md#slices) (FR-UI-005), in `crash-site-area`, drawn with the
viewer's font under every interface layer (no original font art). The rules are `FloaterRules`, `FloaterField` and
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
  original's). Both are saved at once in `user://game_settings_v1.json` (version 2; a version 1 file reads with their
  defaults).

Checks: `FloatersTests` (colours, start offsets and stacking, the fade at its edges, the box and line positions, the
field's 50 ms step and removal, the attack's lines in order at levels 1 and 2, the damage-line formats, dodges, objects
and the attacker's lines), `AreaCombatFloatersTests` (the message conditions from session results, the texts) and the
native smoke stage `floaters` (map mode with `--hero`, only when named: `ARC_AREA_MAP_SMOKE_ONLY=floaters`, observed on
`bates mansion lev 2`; `ARC_AREA_FLOATERS_CAPTURE=<ignored path prefix>` saves readbacks): at level 2 the hero's
critical hit (a granted fate) on the living NPC with the most hit points floats the rules' lines with all 25 texts
from the game's file, the critical-hit message first, stacked from 4h one height apart, the damage line in white; the
smoke's clock raises them 3 px a step and the drawn opacity is the fade's; the NPC then attacks the hero until a hit
lands (red lines over the hero) or is dodged (a white dodge); at level 1 the numbers go, at level 0 nothing is drawn;
it prints `ARC-UI-021 native-smoke floaters PASS`. Not done: spell, trap and script damage and the other damage steps
without an attack (spell blinding included), the draw offsets of the boxes' objects (the hero's while walking), the
original's font art 229, text
bubbles (Text Duration) and combat taunts. Original visual parity NOT VERIFIED.

### Inventory window, pickup and loot (ARC-UI-004)

In `crash-site-area` the [session inventory](../world/inventory-runtime.md) (ARC-WORLD-014)
is wired to the viewer: items can be picked up from the ground and taken from dead NPCs (since ARC-WORLD-101 also from containers), and
the original Inventory key shows what the PC took (FR-ITEM-001, FR-UI-004). Code:
[`AreaInventoryHost`](../../tools/terrain-viewer/Core/AreaInventoryHost.cs) (Godot-free) and
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
  modal like the dialog panel (actor gate closed, clock paused, palette chord refused,
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
  lookup knows the hero's kit and taken items (ARC-UI-016) and the shown map's NPCs' (ARC-UI-031); the same triggers fire the worn
  items of the cast host's own spell damage, payments and passes (ARC-UI-031). Wear values come from the hero load's kit (the best-items start's) and a
  taken item's object row; a made item has none here (NOT READY). The header adds the original's weight lines (IW9): the carried
  weight and the encumbrance level with the weight up to which it holds (`CarryRules.Threshold`). Without a hero the doll reads
  "no hero: nothing can be worn". Stack splitting is built for the drop and a dead NPC's take (ARC-UI-029, below); not built yet: the gold drag. NOT READY with a worn taken
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
  wear location) uses it with the hero as target; a worn or wearable item does nothing (IW5). A held item released on *Use* is used with
  the hero as target while Shift is held and with no target otherwise; an item whose use is a throw is refused there (the throw from the
  map, IW18, is NOT READY). In a turn-based round both need the hero's turn. The use is the original's 0x462CC0
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
  nothing. The moves are the session's `TakeGoldFromNpc`, `TakeAmmoFromNpc` and `DropHeroAmmo`
  ([stack drop](../world/inventory-runtime.md#stack-drop-and-the-quantity-window-arc-ui-029)). *Take all* moves whole stacks
  without asking, a gold pile too (QW1; a session without a hero refuses the gold, as it has none to hold it); a stack of one unit moves at once by the same stack path. The new stack keeps the moved
  stack's kind (the original makes the type's base prototype, QW5; every installed stack is one). A dead NPC with two gold piles
  answers with its first pile (the session's `TakeGoldFromNpc`, NT1), so a take from the second asks with its coins but moves from
  the first (no installed NPC holds two). The loot list
  shows a gold pile's coins (×N) and the header the hero's gold and its four ammunition counts (arrows, bullets, charges, fuel; IW9).
  NOT READY: the question in the barter window and for a junk pile's stacks, a drag of the hero's gold, the art of the original's
  bar. The self-test takes 2 of 12 synthetic arrows by keypad and buttons, cancels with Escape, and drops 1 of the 2
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
  NPC's, junk pile's or container's window (not the steal window) shows the loot window's look in the same frame: the `Loot` art (234)
  on the left with the body's image at (183, 33) (by race or critter flags, a container by its description), the body's grid of 10 × 8
  cells from (8, 136) with each item's inventory art at its inventory location, over its art's cells (ARC-UI-055, `QueryItemCells`; a junk pile's at its pile slot; PROJECT: an item the
  session keeps at no location at the first free place; worn items left out, as the original's grid view), cut
  to the shown rows and scrolled by the mouse wheel (maximum 4 rows, a container 88), the Take all button (808) at (301, 47), and the
  hero's pack, grid, counts and boxes as in the own window (no doll, no weight lines). A body's item pressed and released on the hero's
  grid is taken through the same path as its Take button (a stack asks how many; PROJECT: into the first free cells); released on a
  box (which shows the refused art 348 / 347) or elsewhere it goes back; a right click uses nothing here. The "Diagnostic view"
  switch keeps the Take buttons. NOT READY: the doll views and their toggles, the scroll bar's art, a hero's item moved into the body,
  a follower's portrait, the steal window. The native smoke checks every Crash Site body's window
  (`ARC-UI-052 native-smoke loot-art PASS`: loot arts and the image read, every grid item's art, one item taken by a drag).
- **Round view (ARC-UI-054,** [ledger IW33–IW34](../research/ledger/ui-audio.md#round-view-arc-ui-054); `AreaInventoryHole`): the
  window's open centres the camera on the PC, and in the original look of the own window and the loot window the 89 × 89 rectangle
  at (11, 9) or (16, 17) shows the screen's square of 178 game pixels around the PC (centred 15 game pixels above its foot point) at
  half size, copied by a screen-texture shader on canvas layer 3 under the window (the background art leaves the square out, as
  the original's copy replaces it; the doll art 223 is opaque there), with art 233 or 232 drawn over it when it reads.
  A left release in the rectangle closes the window (in the loot window a held item goes back first). The message line under the
  frame keeps one line in the original look (cut with an ellipsis), so a message such as `took N item(s)` no longer shrinks the
  frame. ARC-UI-060: the rectangle, its art and the release are a table by the window's mode (`AreaInventoryMode`: own (11, 9) and 233,
  barter (16, 17) and 231, loot and steal (16, 17) and 232; `AreaInventoryHole.Release`: nothing held closes in every mode, a held item
  goes back and the window closes outside the own window); the barter and steal windows draw the diagnostic look, so their round view
  shows once their original look is built. NOT READY: the own window's map drop and throw from it (IW18: the held item goes back and
  the window stays open). The native smoke
  checks the scale and the round view on the Crash Site bodies and the round view in the own window, and the release on the first body
  (`ARC-UI-054 round view release closed the loot window`); the own window's release is covered by the unit test of the rectangle only.
- **Dialog.** Virgil's negative `IN` (the ring) goes to the same session inventory through
  `SessionDialogInventoryPort` (the conversation host's `Inventory` port), so the ring
  appears in the window, is saved by F7 and comes back with F8.
- **HUD.** The status line shows `PC items <n>` and the last pickup/loot result.

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

`crash-site-area` and `world-diagnostic` can draw characters and objects from an HD pack
([format](../formats/hd-pack.md)) under the rules of the
[HD presentation contract](hd-presentation-contract.md) (FR-PRODUCT-004). Code: Godot-free
[`HdFrameSource`](../../tools/terrain-viewer/Core/HdFrameSource.cs),
[`HdResidency`](../../tools/terrain-viewer/Core/HdResidency.cs),
[`HdPresentation`](../../tools/terrain-viewer/Core/HdPresentation.cs) and the diagnostic
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
`crash-site-area`, following the toggle protocol of the
[HD presentation contract](hd-presentation-contract.md#toggle-protocol). It changes textures only:
plans, picking, routes, the wall check, light, animation, the clock and the Session never read it, and
it is not part of a snapshot or quick save.

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
- **Persistence.** The request is saved at once to `user://visual_profile_v1.json`
  (`{"schema":1,"profile":"Legacy"|"Hd"}`, codec `VisualProfileCodec`): a missing, unreadable or unknown
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

In `crash-site-area` Virgil talks through the [session party](../world/party-runtime.md): his `JO` joins it, and his
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

In `crash-site-area` the PC walks or runs ([walking and running](../world/gait-runtime.md)): each step takes the
human ART's average time for its gait (walk 547/773 ms, run 168/235 ms per axis/diagonal step), and a run step
draws the run ART (animation 6, loaded only in this mode). A ground click walks; Ctrl+click runs once (walks once
with Always Run on); Num Lock flips the always-run lock; the palette's **Always Run on/off** is the original option
(`user://always_run_v1.json`, `ALWAYS_RUN_TEST_PATH`); clicking an object always runs the approach. A following
Virgil runs while he is farther than range + 2. Since ARC-SKILLS-008 the palette's **Prowl (stealth) on/off** toggles the
hero's and its followers' concealment ([stealth](../world/stealth-runtime.md)); while concealed, a run becomes a walk unless the
hero is a master of prowling with fatigue left (the crouch and the stealth walk animations are not drawn). HUD: `Gait: Always Run …, Num Lock run …; next ground click …;
current gait …` (with `prowling: walks only` or `prowling: a master, runs allowed` while concealed) (in `world-diagnostic` the palette row says it is unavailable and Num Lock stays a reserved key). On macOS a physical
Ctrl+click arrives as a right press with Ctrl held and counts as Ctrl+LMB. Native smoke `ARC-WORLD-020 native-smoke run PASS` (after the quick-save phase, before the follow
stage; `ARC_AREA_RUN_CAPTURE=PATH.png`). ARC-WORLD-021 adds the window vault (a window step takes 1.125 s and draws the
vault ART); the Crash Site maps have no jumpable window, so the map-mode smoke observes it (`ARC-WORLD-021 native-smoke vault PASS`,
[gait runtime](../world/gait-runtime.md#window-jump-arc-world-021)).

### Starting hero (ARC-WORLD-027)

`--hero NAME` (crash-site-area, exterior or `--area-map`) starts the area session as one of the twelve original pre-made
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
combat mode first (the exit gate never closes here: no NPC has a combat focus yet), and the cave transfer leaves it as the
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
it the attempt-attack goal (here the same attack); a follower is attacked only with left Alt held; held `,` `.` `/` aim at the
head, an arm or a leg; a corpse is looted only with Alt, a plain click on it does nothing. The cursor over a living NPC is the
closed hand (`attack`, `walk to attack`). The status line names the outcome, damage, the target's hit points, a knockout or
kill and the experience paid. A killed or unconscious NPC is drawn in the last frame of its dying or fall-down animation,
read on demand from the archive its body came from; out of combat mode a plain click loots the corpse. The stick, another
click, a dialog or window, a map change or a hero that cannot act stop the attack. Native smoke with `--hero` (after the
combat-mode stage, exterior and map mode): `ARC-COMBAT-005 native-smoke attack PASS`, or `NO-TARGET` on a map without a
living NPC with combat values.

### Character sheet (ARC-UI-007)

The original Character sheet key `C` (logical key down) toggles a diagnostic sheet of the session's hero in
crash-site-area (exterior, cave and `--area-map`). It shows what the original sheet of the PC shows
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

Since ARC-UI-009 (task S6 of the [skills plan](../research/skills-plan.md)) the sheet has the original's skills page, checked
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

While `crash-site-area` has a game session, the palette's **Save game (save list)** and **Load game (save list)**
open the save and load windows over the session's save folder (`user://saves`, map mode `user://saves/map`, an
automated run its own folder). The original opens these windows from its in-game menu, which is a later task; the
rules are the original's ([ledger SL1–SL8](../research/ledger/ui-audio.md#save-list-arc-ui-010)), the window's
art and layout are the project's.

- **Folder.** `GameSaveLibrary` ([save list](../world/game-session-runtime.md#save-list-arc-ui-010)): the auto-save
  (`quicksave`, the original's `SlotAuto` "Auto-Save" that F7 writes) and numbered saves `slot0000`..`slot9998`,
  each a snapshot file with a details file (description, hero and level, place, game date and time) and a PNG
  preview of the screen fitted into the original's fixed 468×300.
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

Self-test `ARC-UI-010 self-test PASS synthetic save list` (the production app over the synthetic area owner, on a
temporary folder): a new save closing the window, an overwrite asked (a row activation leaves the question open,
Escape refuses, Return accepts) keeping the save's description, keys owned, the F7 auto-save's details (and the
preview when a frame can be read), the auto-save first and not deletable, a load by row under a new session, F8
loading the newest save (the auto-save, then a numbered save made newer; the HUD names the loaded save), a delete
asked first that clears the selection. The native quick-save smoke checks the auto-save's
details on the original data. Not done: the in-game menu that opens the windows (FR-UI-006, the next tasks), the
original window art, sorting or paging options, a text cursor of the original's own, and the original `.gsi` format.

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
does not open and the log says so: `ARC-UI-011 main menu not opened`). The scene's session is the placeholder the original also has before its menu; the menu
replaces it. The windows, buttons, texts, keys and back moves are the original's
([ledger MM1–MM7](../research/ledger/ui-audio.md#main-menu-arc-ui-011)); since ARC-UI-058 the button windows have the original's art
and layout (below), the other windows the project's.

- **Original look (ARC-UI-058,** [plan](../ui/original-windows.md#main-menu-done-arc-ui-058),
  [ledger MW1–MW6](../research/ledger/ui-audio.md#menu-window-art-arc-ui-058); `AreaMenuArtLayout`, `AreaArtFont`, `AreaMenuArtView`):
  with the game's art the main, single-player, new-game and in-play windows draw their background (329 `MainMenuBack`, 331
  `SinglePlayerBack`) over the 800 × 600 screen at the bars' whole factor, centred and black around, and their labels in the
  `Morph30Font` art font centred on (410, 143 + 50 × i): black at (−1, −1), brown at (+1, +1), the dark red face on top, bright red
  under the pointer. The pointer over a label plays 3026, a press 3027, a release over it chooses it; letters, Escape and the
  controller as before (the labels show no letters, as the original's; the focused label is lit at the window's opening and after a
  focus move, PROJECT; the pointer leaving the screen darkens it). PROJECT: hidden buttons leave no gap; a line at the top
  names them and the window's notice. ARC-UI-059: the options window too (below). The quit box, the save and load, browser, new-character, editor and shop windows keep
  the project's panel (plan), as does every window in the synthetic self-tests (no game folder). The menu stage checks it first
  (`ARC-UI-058 native-smoke menu art PASS`: the art read, the labels at their places, the pointer lighting Single Player and a click
  opening it); `ARC_AREA_MENU_CAPTURE=PATH` saves the main window's picture to an ignored path.

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
  main-menu flag) and adopts it as a load adopts its session; the menu closes. The hero's effect rules and kit facts
  come with it, and the scene's arguments name the hero from then on, so a map transfer reads the hero again with the
  map (as `--hero` does). A new game needs the start map at the map list's start, as the original's new game puts the
  PC there: the AREA exterior, or map mode on runtime map 1 (`./arcanum area`'s `Arcanum1-024-fixed`) opened without
  `--sector`; on another map, in the cave, with `--sector` or with a session a transfer carried, Start first loads the
  start scene ([scene flow](#scene-flow-arc-ui-012), ARC-UI-012). The original's start movies, fade, newspaper event and
  random-encounter schedule are not modelled.
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

The way between the main menu and play in crash-site-area (exterior, cave and map mode), as the original's
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
window, Save Game saving, Quit loading the start map again with a fresh session and the main menu reopened, the save
kept, its root Escape exiting). The short window 4 is covered by the core tests only (no turn-based round on another's
turn in the self-test or the smoke). Not done: the start movies, the menu after the game's end (the original restarts
it there; after the hero's death see [Hero death](#hero-death-arc-ui-026)), the original menu art and button sounds.

### Hero death (ARC-UI-026)

What follows the hero's death in crash-site-area (exterior, cave and map mode), as the original's in single player
([ledger PD1–PD9](../research/ledger/ui-audio.md#hero-death-arc-ui-026)); the core is
[`AreaHeroDeath`](../../tools/terrain-viewer/Core/AreaHeroDeath.cs), the art and timing the original's.

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
- **The load box (ARC-UI-028, Steamwoven only).** A project addition the owner asked for
  ([decision](../architecture/decisions.md), switch `LifecycleRuleProfile.DeathResetsToMainMenu`, which keeps the reset
  above under the Original setting, read from the session's rules at the end of the show). Where the show ends, before
  the reset, when the save list holds a save, the black screen shows a box drawn as the main menu's quit box: the
  project's question "You have died. Load the newest save?" (the game's message files hold no such question), the
  newest save's description and name, Yes (Y) and No (N), and a line on the keys. Y, Return, the Yes button or the
  controller's confirm loads the newest save (by file date, as F8 and the main menu's Last Save) as F8 does: on the
  shown map at once, a save on another map after that map loads, while the screen stays black; then the loaded game
  fades in (2 s, or after 2 minutes without the map) and play goes on. A refused load (a damaged save, another map
  loading) keeps the box with the reason under the buttons, and so does a load that brings no living hero (the save's
  map failed to load and the old game came back, or the save's hero is dead); a window stream held under the screen is
  dropped first, as the load replaces the scene. N, Escape, the No button or the controller's cancel ends
  the game with the reset above. Without any save the reset follows at once, as the original. The box acts on key
  presses (the slides on releases); its buttons take the mouse, nothing under the screen does.

Automated runs (self-tests, native smokes) keep the death off unless their own check turns it on, as many of them kill
or wound the hero for other checks. Self-test `ARC-UI-026 self-test PASS synthetic hero death` (the production app over
the synthetic area owner, a synthetic slide table, picture and voice, the start scene published again): a new game's
hero dies, the in-play menu opened meanwhile closes after 5 s and the death comes 0.3 s later, the fade to black owns
the input (Escape opens nothing), the 800 × 600 picture fades in, its voice starts, a key ends it, the reset loads the
start scene with a fresh session and the reopened main menu, which fades in; a hero brought back to life before the
death comes is not reset; a voice's end ends the slide; a start scene that does not publish clears the screen over
the game as it is. ARC-UI-028 in the same self-test: with a save in the list the Steamwoven setting asks after the
slides (the question, the save "Auto-Save (quicksave)", Yes and No; another key does nothing), Y loads the save with its
living hero and no reset, and it fades in; a damaged save keeps the box with the reason and Escape then resets; the No
button and the N key reset; under Original the death resets without the box though a save is there. Native smoke on the original data, map mode on the start map
with `--main-menu true`: `ARC_AREA_MAP_SMOKE_ONLY=death` prints `ARC-UI-026 native-smoke death PASS` and
`ARC-UI-028 native-smoke death load PASS` (a new game as the first pre-made hero saved into a temporary save folder,
the hero killed through the session, the death after 5 s on the wall clock, the installed default slide's 800 × 600
picture with the slides' music and its voice, a left click through the window ending it; under Steamwoven the box asks
and a left click on Yes loads the save, which fades in with its hero alive; the loaded hero killed under Original,
Escape ending the show, the start map loaded again with a fresh session and the main menu reopened, without the box). `ARC_AREA_DEATH_CAPTURE=PATH.png` saves the shown
slide (original art: never into git). Not done: script-queued slides, the end-game and credits slides, the hero's dying
animation.

### Options window (ARC-UI-013)

The original's options window (its window 6, [ledger OP1–OP8](../research/ledger/ui-audio.md#options-window-arc-ui-013))
in crash-site-area, opened by Options in the main menu and in both in-play windows; the art and layout are the
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
  page's first slots in order (the Rules line the Game page's fifth, at (531, 122)); the control with the keyboard or controller
  focus has its label lit in the page buttons' hover red; a text is one line. The line at the top names the hidden controls and the
  notice. Without the game's art (the synthetic self-tests) the whole window is the project's panel.

- **Pages.** The buttons Game, Video, Sound and Done with the game's `MainMenu.mes` labels and letters (messages 4000–4003,
  letters G, V, S, D); the window opens at the Game page each time. The letters act as in the other menu windows, in both
  rule modes; the original's options window ignores them (an original bug: its hotkey path runs such buttons only in
  the in-play windows, OP7), which the owner chose to fix everywhere without a switch (2026-10-28). The Video page (ARC-UI-021) shows Floats and
  Float Speed, the [floating text](#floating-text-arc-ui-021)'s level and speed; its Brightness, Text Duration and Combat
  Taunts are hidden with their reasons (no gamma control, text bubbles or combat taunts).
- **Controls.** Each control shows its `options_ui.mes` label (message 100 + 100 × page + slot) and its value: a
  list's text from its value file (`OptionsDifficulty.mes`, `OptionsCombatMode.mes`, `OptionsOffOn.mes`), a slider as
  `N/10`. Game: Difficulty (Easy, Moderate, Hard), Default Combat Mode (Real Time, Turn Based), Auto Attack, Always
  Run and the project's Rules line (ARC-UI-014, below). Video: Floats (Never, Minimal, Verbose) and Float Speed (0–10). Sound: Effects, Voice and Music (0–10). The arrows (or Left and Right on the focused control, which Up and Down or
  a click on an arrow choose, or a controller's confirm) change the value at once, as the original's setters run on each step: a list wraps around at either end, a
  slider stops there. Hidden with their reasons: Module (one module), Violence Filter, Auto Switch Weapons, Follower
  Skills, and the third combat mode Fast Turn Based (the list cycles Real Time and Turn Based).
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
  together with the difficulty. It is stored in the launcher's settings file (`launcher_v1.json`, `rules.active`),
  which the [launcher screen](#launcher-screen-arc-ui-014) writes too, and every ordinary run starts in it.
- **Storage.** User preferences, never part of a save: difficulty, turn-based, auto attack and the floaters in
  `user://game_settings_v1.json` (`{"version":2,"difficulty":D,"turnBased":B,"autoAttack":B,"floats":F,"floatSpeed":S}`;
  a version 1 file without the last two reads with their defaults; a missing or invalid file reads as the original's
  defaults 1, off, on, 1, 2), Always Run and the volumes in their own files. Each change is saved at
  once (the original writes `arcanum.cfg` when the game exits). Space's toggle also saves the turn-based setting now. An
  automated run (self-test, native smoke) reads and writes no file; `GAME_SETTINGS_TEST_PATH` names one for a test.
- **Closing.** Done goes back to the main menu, or from play closes the menu and play goes on; from play the O key
  does the same. Escape does nothing in this window, as in the original's key table.

Self-test `ARC-UI-013 self-test PASS synthetic options window` (the production app over the synthetic area owner
launched without the menu, no preference file): in-play Options opening the window at its Game page, each arrow
applying the difficulty (on the session), the turn-based setting, auto attack, Always Run, the floaters' level and
speed on the Video page (its hidden controls named) and a volume at once, lists wrapping and sliders stopping, Escape doing nothing, Done and O closing the menu
from play, Done going back from the main menu, the Rules line switching the session to Original and back. Native smoke on the original data, map mode on the start map with
`--main-menu true`: `ARC_AREA_MAP_SMOKE_ONLY=options` prints `ARC-UI-013 native-smoke options PASS` (all 27 option
texts from the game, the original's defaults shown, Original chosen on the Rules line, a music step moving the channel,
Done back to the main menu, a new game as the first hero beginning under the Original rules, then in play a difficulty
step reaching the session, the default combat mode going through the hero's turn-based switch and back, Rules back to
Steamwoven on the session, the O key closing the menu; ARC-UI-059: the art read, the page labels at their places, none lit
while a control has the focus, the pointer lighting Sound and a click on it showing the Sound page; `ARC_AREA_OPTIONS_CAPTURE=PATH`
saves the Game page's picture to an ignored path; ARC-UI-061: the controls at the first slots on the Game and Sound pages, the
pointer lighting Music's left arrow, a press showing it down with 3000 and the release over it playing 3001 and lowering the music;
the capture also saves the Sound page next to it with `-sound` before the extension). Not done: Brightness, Text Duration, Combat
Taunts, Module, Violence Filter, Auto Switch Weapons, Follower Skills and Fast Turn Based (no function yet).

### New-character window (ARC-UI-030)

N10b, slice 1 (FR-CHAR-001, FR-CHAR-003, FR-CHAR-005): the original's window 12, which New Character opens from the New Game
window ([ledger NW1–NW6](../research/ledger/character.md#new-character-window-arc-ui-030)); the art and layout are the project's.

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
  original. Race and gender names are the project's.
- **Portrait** (ARC-UI-033, FR-CHAR-004, [NW9](../research/ledger/character.md#new-character-name-and-portrait-arc-ui-033)):
  a row with the original's arrows over the portraits the race and gender may take (`PortraitCatalogue.Press`, PO9: next
  past the last takes the first, previous before the first takes the last); a race or gender change takes the first
  portrait of the new pair, a refused press keeps it. The window starts with the generic prototype's portrait 1005 and
  shows the portrait's image name and number (the image is not drawn yet). The catalogue is read with the generic start
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

Since ARC-UI-034 Accept opens the [character editor](#character-editor-in-creation-mode-arc-ui-034) instead of starting the
game, and since ARC-UI-036 the editor's Accept opens the [opening shop](#opening-shop-arc-ui-036), whose kit (the background's
money and items, the clothes, lockpicks and bandages) the new character starts with. Not built yet (later N10b slices): the
portrait's image in the window; the body type and art by race and gender are N15. Self-test `ARC-UI-011 self-test PASS synthetic main menu` covers the window (its rows, arrows, Escape
back, the default name refused, a name typed through the keys and an Accept that opens the editor and, through it, starts a
dwarf with a background and its effect, the name and the portrait); native smoke on the original data: the `menu` stage
(`ARC_AREA_MAP_SMOKE_ONLY=menu`) also opens New Character, presses gender on a dwarf man (refused; the background stays under Steamwoven) and makes an elf woman with a background,
steps her portraits, has Accept refuse the default name, types a name through the window's keys, accepts, spends in the
editor and checks the session (`ARC-UI-030 native-smoke new character PASS`, `ARC-UI-034 native-smoke character editor PASS`,
then `ARC-UI-033 native-smoke name and portrait PASS`). Checks: `AreaNewCharacterTests`, `CharacterNameRulesTests`,
`HeroNameTests`, `AreaStatsPageTests`.

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
  rules and texts, [skills page](#skills-page-arc-ui-009)), and Cancel and Accept. Each press that changes the draft is
  recorded; a refused one is not.
- **Accept** (or Return, CM5) opens the [opening shop](#opening-shop-arc-ui-036) (window 15, CM2), whose Accept starts the new
  game as Start does for a pre-made hero, then replays the recorded presses on the new session inside an editor of its own
  opened on the same start (`AreaNewCharacter.Apply`), and closes that editor: the game starts with nothing refundable. A press
  the new session refuses fails the start and names it. The draft and its editor stay, hidden, while the shop is open.
- **Cancel** (or Escape, CM5) goes back to the new-character window, which, as in the original, starts a new character (the
  generic human man; CM6). The name field starts with message 500 again (the original keeps the typed name, NW7: a later
  slice).
- **Keys.** The menu owns the keys: Return accepts, Escape cancels, no letters (CM7). A controller moves over Accept and Cancel
  and confirms; the pages' buttons take the pointer.

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
- **Not built.** The bought wares keep their facts only for the start's wield: in play a ware the regeneration made has no wear,
  use or move facts in the viewer (as any made item, NOT READY) and one from a shop row has them only while the shop map's rows are
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

### Interface bars (ARC-UI-018)

In crash-site-area (exterior, cave and map mode) the original's in-game interface bars are drawn over a session with the
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
| Skills | bottom | A toggle (Spells goes up when it goes down); the original's skill pane is not built, so it only says so. |
| Fate Points, Sleep | top | Say that their windows are not built. |

A button shows its idle, down and lit frames as the original's (GB4). It plays 3000 on the press and 3001 on the release
over it, and acts on that release; a toggle plays 3000 turning on and 3001 turning off (GB3, GB6). The pointer over a
button writes its name from `mes\intgame.mes` (1000–1009) where the original's message window is; there is no hover sound
on the bars. The main menu's, in-play menu's and options window's buttons play 3026 when the pointer enters one and 3027 on
the press (GB7). The bars are drawn over the windows they open, and those windows (character sheet, inventory, logbook,
schematics) lay themselves out in the part the bars leave free; while a conversation, barter, reading, the save list,
the palette, the menu or the world map is open the bars are hidden, and they come back with no button lit or held. The
diagnostic HUD moves below the top strip and the follower portraits and spell pane above the bottom strip. Since ARC-UI-020 the health and fatigue vials show the PC's
[gauges](#health-and-fatigue-bars-arc-ui-020), and since ARC-UI-023 the bottom strip's message window is the
[message log](#message-log-arc-ui-023), where the button names appear. Since ARC-UI-027 the hot-key slots, the recent-action buttons and
the light button are the [hotkey bar](../world/spell-cast-host.md#the-hotkey-bars-art-and-drag-arc-ui-027) (icons, counts, clicks and the
right-button drag). Since ARC-MAGIC-026 the top strip's five slots are the
[maintain bar](../world/spell-cast-host.md#the-maintain-bar-arc-magic-026): they show the hero's maintained spells and a click on one
ends its spell. The top strip's clock is drawn as the art shows it and does nothing yet. Automated
runs draw no bars unless their smoke asks.

Tests `AreaGameBarTests` (layout, frames, button states and sounds, texts, interface art names; with `DAT_ROOT` the
installed art and texts read). Native smoke on the original data, map mode on the start map with `--main-menu true`:
`ARC_AREA_MAP_SMOKE_ONLY=bar` prints `ARC-UI-018 native-smoke bar PASS` (through the window's GUI: a menu button's 3026 and
3027, a new game as the first pre-made hero, the bars with all 11 arts and 10 texts from the game, Character's hover
name and lit frame, its down frame with 3000, the release with 3001 opening the sheet, Inventory switching from the sheet,
Logbook and Schematics opening and closing their windows, the logbook lying between the strips and taking a press inside
it, Map opening the world map with the bars hidden and back with no hover, Combat in and out of combat mode, Spells showing and hiding
the pane, Fate Points refused, a press on the strip's clock not reaching the world, every sound started). Not done: the
hot-key bar (later slices; the vials since ARC-UI-020, the message log since ARC-UI-023, the vials' art, the follower frames
and the hovered portrait since [ARC-UI-024](#panel-art-arc-ui-024)), the skill pane, fate and sleep windows, the town map, the
buttons' lit art the original sets from game events (0x556A90), the toggle to the compact interface (the viewer shows the
compact panel whenever its bars are not drawn).

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
  by characters until it fits. The original's font art (27 for titles, 229 for text, both white) is not drawn: the viewer's font
  stands in at its sizes.
- **Scrolling.** The window's upper half (210, 504, 382 × 41) shows the next older entry, as far back as the log holds, and its
  lower half (210, 545) the next newer one; both are invisible push areas with no sound that act on a release over the area
  pressed.
- **Button names.** The pointer entering a bar button and every move while it is there write the button's name
  (`mes\intgame.mes` 1000–1009) as a hover entry with icon 444, which is never stored and shows only in the message mode and when
  no entry was stored in the last 3 s (real time). Leaving the button takes the name away.
- **Panes.** While Spells (the spell pane, `M`) or Skills is down the window belongs to that pane and draws nothing of the log
  (since ARC-UI-053 the spell pane is drawn there as the original's bar mode 1, with its art, college buttons and spell icons:
  [spell pane](../world/spell-cast-host.md#the-spell-pane-in-the-bottom-strip-arc-ui-053); the skills pane's art is a later slice). A new stored entry puts the
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
logbook of the session's PC in crash-site-area (exterior, cave and `--area-map`), with the two tabs of the original that
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

**Not implemented:** the other five tabs (reputations, blessings and curses, kills and injuries, background, keys), the
book pages and page buttons (the viewer scrolls one list), the developer quotes page, the logbook button's highlight, the
sounds, the PC lens, `RU`/`RQ` as reply conditions, scripts' rumor actions. Self-test `ARC-UI-008 self-test PASS`
(synthetic texts: open without a hero, order and styles, `Tab`/controller tab switch, keys owned, tab kept and reset,
low-intelligence texts, rebuild on change, dead-hero close and refusal); native smoke after the hero stage (exterior and
map mode, with or without `--hero`): `ARC-UI-008 native-smoke logbook PASS` (the module's texts load; `L`, `Tab`, `Escape`
on the live session; on a detached copy every quest and rumor that has a text is listed with it).

### Schematic window (ARC-TECHNOLOGY-005)

The original Technical schematics key `T` (logical key down; the original's in-game key handler opens the window for the local
PC as crafter and viewer, ledger [SW1](../research/ledger/technology.md#schematic-window-arc-technology-005)) toggles
a diagnostic schematic window of the session's hero in crash-site-area (exterior, cave and `--area-map`), with the learned and
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
(crash-site-area: exterior, cave and `--area-map`; it needs `--hero`). Contract: [barter runtime](../world/barter-runtime.md), ledger
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

**Not implemented:** the original window art, grids, paperdoll and drag and drop, the count dialog, the quote lines, the gamble slot,
the follower menu and cycling ([barter runtime](../world/barter-runtime.md#not-modelled-not-ready)). ARC-WORLD-096: every open schedules
the restock of the window's container (else the merchant), logged as `ARC-WORLD-096 restock schedule`, and a loot open schedules the
looted holder's; the event world runs the restock through the viewer's restock host ([barter runtime](../world/barter-runtime.md#restock-in-the-viewer-arc-world-096)). Self-test
`ARC-UI-050 self-test PASS` (synthetic hero and merchant: modal open with both quotes, keys owned, a sale and a buy through the session
with the trade sound, `Escape`, a replaced session closes it); native smoke `ARC-UI-050 native-smoke barter PASS` (stage `barter` of
`ARC_AREA_MAP_SMOKE_ONLY`, `--area-map` with `--hero`: a speaker opened through the path a `b:` reply takes, a merchant buying through
its substitute container when no speaker buys itself, its quotes, the open is at most two revisions (the coins and identification, the
restock schedule), `Escape` closes with nothing changed; one sale and one buy on a detached copy, through the container when there is
one); then `ARC-WORLD-096 native-smoke restock PASS` on the live session: a jump to the container's restock event with the container on
screen schedules it again, with the camera panned until it is off screen the next event regenerates it through its merchant, and the
window opened again shows the new wares; then `ARC-WORLD-103 native-smoke redress PASS`: the first living NPC of the map with an
inventory source of its own whose set the project admits and combat values is scheduled through the window open's path, its event regenerates it and it wields its
best items through the production event world, and the session's list shows the slots its record names (SKIP on a map without such an
NPC; [NPC best items](../world/barter-runtime.md#npc-best-items-after-a-regeneration-arc-world-103)).

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
5, `user://audio_volumes_v1.json`; automated runs use no file unless `AUDIO_VOLUMES_TEST_PATH` is set),
the catalogue and the last result. Self-test marker `ARC-UI-040 self-test audio PASS`; native smoke
marker `ARC-UI-040 native-smoke audio PASS` (exterior AREA and `--area-map`).

### Music and ambient (ARC-UI-041)

The session's music and ambient schemes play in crash-site-area (exterior, cave, `--area-map`) as in the original:
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
| Always run, visual profile, audio volumes, game settings `game_settings_v1.json` (ARC-UI-013) | `user://` | none, as before (off, Legacy, defaults) |
| HD pack folders | discovered | not searched, as before |

So a zoom the player saved (for example 1.70) no longer changes a self-test, and a self-test's own zoom steps no longer
land in the player's file. The self-test checks it first and fails with `SELF_TEST_SETTINGS_NOT_ISOLATED` when the zoom or
input store would use a `user://` path; the log prints `ARC-DEV-032 self-test PASS settings isolated (the zoom and input stores use no user:// file)` and
`ARC-DEV-032 automated run: settings and saves are isolated from the player's files` when the folder is first used. The
WORLD settings validation launch still requires both explicit test paths. Checks: `ViewerStoragePathsTests`.

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
