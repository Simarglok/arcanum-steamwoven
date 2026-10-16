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
184 identities (4,280 actor-mode total; 4,285 world-mode total), and the unchanged 256 MiB
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
closes the dialog. The HUD says whether a dialog NPC is in reach and what the dialogs left in
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
groups `[0,7,6,5,4,5,6,7]` and mirror only slots 1..3. Action changes reset frame/interval;
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
restart tests do not overwrite ordinary preferences.
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
ARC-WORLD-009 build (`./arcanum area` prefers it); exported AREA builds that predate
it reject the flag ([launcher](../workflow/local-development.md#cave-and-return-candidate-arc-world-009)).

### Actor anchor and wall fade (ARC-RENDER-011)

**Actor anchor.** CE places a map location at `location_xy = 40(y-x-1), 20(y+x)` and
draws an object at that point plus its offset plus `(40,20)`, so every object, the
player included, stands on the centre of its tile diamond. The AREA plan projects
terrain and bodies from `40(y-x)`, 40 px to the right of `location_xy`, while the
actor placement restates `location_xy` itself; before this change the actor was
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

**Wall check.** The original fades walls in front of the player. At the pinned research
revision: CE `src/game/wallcheck.c` (block regions 47-60, corner offsets 63-91, edge
table 94-560, `wallcheck_recalc` 712-910, wall-flag update 941-1106), `object.c`
(wall skip under a faded roof 560-575, wall render alpha 4336-4400, tile object lists
3304) and `first_party/tig/src/art.c` (alpha lerp 3221-3260 and 3486-3493).
`AreaWallCheck` (viewer Core) restates it:

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

The edge list is the original's data (decoded from CE's table) re-expressed as
lattice geometry; no CE code or structure is copied. A literal research port of the
CE function gave the same fades as `AreaWallCheck` for every tile of the cave sector
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
`user://saves/quicksave.json` (Godot user data of the viewer). There is no save
list, naming, confirmation or autosave. Elsewhere F7/F8 stay reserved.

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
- **HUD.** The area status line shows the slot and the last F7/F8 result.

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
  original's screen pick does ([CE `sub_43D9F0`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L1792-L1990)).
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
  ([CE `intgame_refresh_cursor`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/intgame.c#L6150-L6190)).
  The decision follows `WorldRouteClick` in its order and guards without running it:
  a dialog NPC is *talk* within the 3-tile reach, else *walk to talk*; the cave
  entrance or exit is *use* within use range 2, else *walk to use*; anything else is a
  ground goal: *no route* when the route planner refuses it without a search (the cell or
  the actor outside the area, the goal more than 32 cells from the actor on an axis, or no
  neighbour step entering the cell), else *walk here*. The bounded search itself is not run
  on hover, so a *walk here* goal can still end without a route; the reach is measured from
  the committed cell, while during a step the route plans from the step's target cell, so a
  goal exactly at the 32-cell edge can differ by one. Since ARC-UI-004 an item on the ground
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
  the source's range for opening a container, PC or NPC
  ([CE `sub_428930`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L6500-L6531));
  the pickup goal's own move-near range is not pinned, so pickup uses the same reach (project
  choice). Living NPCs, chests (contents generated on first open, not modelled) and items
  inside holders are not targets. A ground item the session refuses (gold, ammunition, keys,
  a pickup or insert script; none on the exterior core) is still shown as *pick up* and the
  click reports the refusal. Not modelled: the pickup animation and action-point cost.
- **Taken items.** An item the PC took is neither drawn nor picked any more (the canvas
  skips its body; the hover pick passes over it), also after a quick load. The resource
  sweep of the native smoke counts those bodies as taken.
- **Window.** `I` (the original Inventory key, logical key down) toggles it while the area
  inventory exists; I, Escape, the Close button or a controller cancel close it. It is
  modal like the dialog panel (actor gate closed, clock paused, palette chord refused,
  every key owned by the window). Left: the PC's list in the order items entered it, each
  with its display name (the hover rule: `object_examine` order from the description
  tables, type fallback without them), type and weight. When looting, right: the dead NPC's
  current list with a *Take* button per item and *Take all*; a refusal stays on the body and
  is named in the message line (on the installed exterior the item with its own
  `SAP_INSERT_ITEM` script: "the item runs a script on this transfer"). Project
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
admitted cell with life evidence, every core ground item and dead NPC a target, Virgil's ring through the viewer's conversation host, 5 of 6 loot,
37 pickups, save and load), the self-test `ARC-UI-004 self-test PASS` (synthetic seed over the
production click path: walk-to-pickup hover and press from 2..6 cells, its cancel by a ground
click, pickup next to the item, I/Escape, loot by the take button) and, with the cave input, the native smoke `ARC-UI-004 native-smoke inventory PASS`
(one pickup next to an item, one walk to pick up, the four dead NPCs looted with *Take all*:
5 taken, 1 left, every taken item named in the window; `ARC_AREA_INVENTORY_CAPTURE=PATH.png` saves a
readback of the open window) and the quick-save phase, which now
also checks that F8 brings back the PC's items. Not verified: original inventory UI parity
(grid, drag and drop, paper doll), a human on a physical mouse and display.

### Virgil follows the PC (ARC-WORLD-016)

In `crash-site-area` Virgil talks through the [session party](../world/party-runtime.md): his `JO` joins it, and his
follower entry (node 385) offers its `WA`/`UW` replies. While he follows he walks after the PC on his own grid session
over the same step evidence (start beyond 4 cells, stop within 2, stand on the PC beyond 30 then step off); the PC and
Virgil block each other where they stand, and his T0 cell no longer blocks. His T0 body frame is drawn where he stands
(no walk cycle or turning), talk reach uses his current cell, a waiting Virgil stays, and his cell is written to the
session on F7 and when the PC enters the cave (he does not come along yet). HUD: `companion script 1324 <standing> at
(x,y) …`. Contract and checks: [companion follow runtime](../world/companion-follow-runtime.md); native smoke
`ARC-WORLD-016 native-smoke follow PASS` (with the cave input, after the quick-save phase;
`ARC_AREA_FOLLOW_CAPTURE=PATH.png` saves the frame).

## Verification and evidence status

### WORLD camera zoom (ARC-GPAD-004)

WORLD starts in Follow at preferred zoom 2.00 when no valid preference exists.
This is a hypothesis for an assessable actor, not a QHD comfort measurement.
LT out / RT in are admitted complete-frame levels with deadzone 0.10 and full
drive one octave/second. The ratio remains 0.125..8 with nearest filtering;
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

Separate `user://world_diagnostic_zoom_v1.json` stores numeric
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
placement conventions are attributed CE-derived specification evidence at the
pinned research reference; no CE code/runtime or mechanical translation is
included, and this makes no clean-room or independent-corroboration claim.
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
