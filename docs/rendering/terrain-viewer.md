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

The viewer has no picker, archive discovery, precedence, PATCH/dif/loose-file
policy, or extraction behavior. It requires caller-selected inputs and a safe,
non-path source label:

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
  old position. The area status line repeats it as `hover <name> -> <action>`.
- **Names.** The description tables come from the content stack of the game folder that
  holds the area's module archive (`<game>/modules/<Name>.dat`, the installed layout; the
  mounted module archive must be that same file). They load once in the background
  (about 0.2 s on the owner's installation) and are kept across reloads and the cave.
  Without them (another layout, a mount or parse failure) the tooltip names the object
  type, e.g. `[NPC: name unavailable]`, and the status line gives the status (never a path).
- **Action cursor.** Project policy, not original behaviour: the original main-mode cursor
  changes with weapon and called-shot keys, not with the target
  (the cursor refresh, 0x553990).
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
  is *pick up* within reach 1, else *walk to pick up*, and a dead NPC is *loot* or *walk to
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
verified: original cursor or message-window parity, hover art (the animated underlay), a
human on a physical mouse and display.

### Inventory window, pickup and loot (ARC-UI-004)

In `crash-site-area` the [session inventory](../world/inventory-runtime.md) (ARC-WORLD-014)
is wired to the viewer: items can be picked up from the ground and taken from dead NPCs, and
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
  choice). Living NPCs, chests (contents generated on first open, not modelled) and items
  inside holders are not targets. A ground item the session refuses (gold, keys,
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
  diagnostic window: no inventory grid, equipment, drop, stacks or weight; the insert check
  accepts every item (carry weight, Technophobia and grid room need the created character,
  NOT READY).
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
click, pickup next to the item, I/Escape, loot by the take button) and, with the cave input, the native smoke `ARC-UI-004 native-smoke inventory PASS`
(one pickup next to an item, one walk to pick up, the four dead NPCs looted with *Take all*:
5 taken, 1 left, every taken item named in the window; `ARC_AREA_INVENTORY_CAPTURE=PATH.png` saves a
readback of the open window) and the quick-save phase, which now
also checks that F8 brings back the PC's items. Not verified: original inventory UI parity
(grid, drag and drop, paper doll), a human on a physical mouse and display.

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
(no walk cycle or turning), talk reach uses his current cell, a waiting Virgil stays, and his cell is written to the
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
walks, and the viewer does not run the session's event queue, so that fatigue only returns with a quick load. In combat
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

**Not implemented:** spending unspent points on stats, hit points or fatigue (the original's +/− buttons and their
errors), tech disciplines and schematics panels, age (not session state), the portrait and PC lens,
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

The view-only and character-creation modes of the original sheet have no caller in the viewer. Those modes also show `−` down
to rank 0.

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

The message window (`AreaMessageWindow`, bottom centre) is the smallest bounded stand-in for the original's: the newest entry
with its title and lines, a history of the last ten browsed with ‹ and ›, and × hiding it until the next entry; its buttons take
no focus, and a click elsewhere on it reaches the world. A different session (a quick load, a new game) empties it and its
queue, and so does a session that ends without one (a failed reload); a map transfer, the main map's window move or world-map
travel carries the session and keeps them. No other original
message uses it yet.

Self-test inside `ARC-UI-007 self-test PASS` (synthetic hero: an award to level 3 posts two entries with the project's labels,
eleven more keep the last ten); native smoke `ARC-UI-046 native-smoke level message PASS` on the original installation (all six
files load; an award built in the smoke, without changing the session, posts a points entry and a scheme entry that read the
player's title, formats and names, each with the level-up sound; no text is printed). Not ready: the original's message
window art, its other messages and its three interface callbacks after the entry (button state, two refreshes, not traced).

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

### Main menu (ARC-UI-011)

With `--main-menu true` (launcher: `./arcanum area`, which opens it by default since ARC-UI-012 unless `--no-main-menu` or `--hero`, or `./arcanum map NAME --main-menu`; refused with
`--hero`, as the menu picks the hero) the AREA viewer opens the original's main menu over the scene once it is ready,
the first time in a run (when that first scene has no game session, for example a map outside the map list, the menu
does not open and the log says so: `ARC-UI-011 main menu not opened`). The scene's session is the placeholder the original also has before its menu; the menu
replaces it. The windows, buttons, texts, keys and back moves are the original's
([ledger MM1–MM7](../research/ledger/ui-audio.md#main-menu-arc-ui-011)); the art and layout are the project's.

- **Windows.** Main (Single Player, Exit Game), Single Player (New Game, Load Game, Last Save, Cancel) and New Game
  (Pick Character, Cancel), in the original's order. The original's MultiPlayer, Options, Credits, View Intro and
  New Character are hidden, as Steamwoven does not have them yet (network play, the options window of the next
  tasks, credits, movies, the character editor); each window names its hidden buttons and why. A button opens its
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
named, as the open menu blocks the other stages. Not done: the options window (N9), the character editor,
credits, movies and network play, the original menu art and button sounds; the scene flow is
[ARC-UI-012](#scene-flow-arc-ui-012).

### Scene flow (ARC-UI-012)

The way between the main menu and play in crash-site-area (exterior, cave and map mode), as the original's
([ledger SF1–SF5](../research/ledger/ui-audio.md#scene-flow-arc-ui-012)); the art and layout are the project's.

- **In-play menu.** In play, Escape (no palette, dialog or window open, the window focused, no load in progress)
  opens the same menu at the original's in-play window: Save Game, Load Game, Quit and Continue, with the game's
  `MainMenu.mes` labels and letters (messages 30–34); Options is hidden until the options window (N9). In a
  turn-based round on another combatant's turn it opens the original's short window (Quit and Continue, messages
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
turn in the self-test or the smoke). Not done: the options window (N9), the start movies, the menu after the hero's death
or the game's end (the original restarts it there), the original menu art and button sounds.

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
`VISUAL_PROFILE_TEST_PATH`, `AUDIO_VOLUMES_TEST_PATH`, `ARC_HD_BUDGET_PATH`); otherwise an automated run uses a fresh
temporary folder of its own process (`arcanum-viewer-automated-<pid>-<id>` under the system temporary folder, deleted when
the viewer app exits); only an ordinary run uses the `user://` file. Per setting:

| Setting | Ordinary run | Automated run without a test path |
| --- | --- | --- |
| Input preferences `world_diagnostic_input_v1.json` | `user://` | the run's folder (starts at the WORLD defaults) |
| Zoom `world_diagnostic_zoom_v1.json` | `user://` | the run's folder (starts at 2.00) |
| Quick saves `saves/`, `saves/map/` | `user://` | the run's folder (stages that test saving use their own slot as before) |
| HD budget `hd_budget_v1.json` | `user://` | none (the contract ceilings) |
| Always run, visual profile, audio volumes | `user://` | none, as before (off, Legacy, defaults) |
| HD pack folders | discovered | not searched, as before |

So a zoom the player saved (for example 1.70) no longer changes a self-test, and a self-test's own zoom steps no longer
land in the player's file. The self-test checks it first and fails with `SELF_TEST_SETTINGS_NOT_ISOLATED` when the zoom or
input store would use a `user://` path; the log prints `ARC-DEV-032 self-test PASS settings isolated (the zoom and input stores use no user:// file)` and
`ARC-DEV-032 automated run: settings and saves are isolated from the player's files` when the folder is first used. The
WORLD settings validation launch still requires both explicit test paths. Checks: `ViewerStoragePathsTests`.

The same automated runs keep drawing, at full speed, while the window cannot draw (minimized): see
[minimized window draw](../workflow/toolchain.md#minimized-window-draw-arc-dev-045) (ARC-DEV-045).

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
