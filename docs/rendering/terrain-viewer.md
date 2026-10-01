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

`--mode world-diagnostic` (ARC-WORLD-001) combines the same terrain, exactly
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

## Integrated world diagnostic policy

WORLD is free diagnostic movement, not gameplay or collision. State is double
sector-local cell-center `(X,Y)`, closed `[0,63]` on each axis, reset `(30,32)`,
Idle frame 0/facing 0. Projection is `(40*(Y-X),20*(Y+X))`; inverse screen delta
is `(v/40-u/80,v/40+u/80)`. Speed is 240 unzoomed projected pixels/second at
sensitivity 1. Circular deadzones and linear sensitivity are applied once;
screen diagonals are normalized. Analytic boundary segments slide tangentially
without substepping; completely blocked displacement selects Idle. Zero-time
input cannot move or create Walk; explicit neutral may stop Walk at frame 0.
Invalid/overflow steps commit neither actor state nor animation clock.

The project diagnostic screen octants are clockwise up=0, up-right=1, right=2,
down-right=3, down=4, down-left=5, left=6, up-left=7, with clockwise half-octant
ties. They do **not** establish original compass semantics: source-facing
numbers remain opaque. Existing physical group/mirror admission is unchanged.
Idle plays at a diagnostic 6 FPS/15 frames; Walk at 10 FPS/8 frames. Raw source
8/15 FPS metadata remains separate. Action changes reset frame/interval;
same-action facing changes preserve the interval. The last moving segment
determines facing. Cell floor and ToEven fractional projected pixel offsets
bridge to the checked source-derived normal placement; camera origin stays 0.
Follow targets continuous projected baseline plus `(0,20)`, not rounded sprite
position. No sprite repositioning/visual nudge substitutes for source geometry.

WASD/left stick moves the actor. Arrows/right stick pans the world image and
immediately selects Manual, even at zero dt or a clamped camera. Simultaneous
actor/camera intent remains independent. Releasing pan while actor remains
held stays Manual; both domains neutral arms return without snapping; the next
actor-only activation selects Follow. Pan cancels stale return. Held opposing
keys count as activity even when their net vector is zero. The double camera
center clamps to a stable envelope of terrain, five statics and **all** actor
corner/frame/mirror extents, including rounding guard; small maps center at
their midpoint. Movement never changes the envelope/cache or reads archives.

Left drag selects Manual without moving the actor; wheel/+/- changes bounded
zoom without changing Follow eligibility. HUD/settings consumes pointer input;
UI keyboard focus blocks world input. Resize clamps the center to viewport
bounds but never derives actor state from `Camera2D`. `F` fits and remains
Manual/unarmed; `R` resets scene and Follow without resetting preferences;
`L` reloads through generation-aware loading. F11/Escape use ordinary window
controls. Focus loss/gain, modal transitions, active disconnect/acquisition,
reset/reload/failure/exit stop animation and quarantine stale input. Inactive
disconnect preserves scene. Rearm requires two complete observed-neutral frame
samples of every mapped keyboard and active-controller level; cached/event or
opposing-key cancellation is not neutral proof. Live sampling uses pinned
Godot APIs, not simulated levels. An incomplete, missing-active-controller or
invalid observation quarantines **before** reducer timing or UI actions: Idle
frame 0, cleared drag/navigation and Manual-return arm, with last valid actor
position and bounded camera center preserved. Held input on recovery cannot
substitute for those two complete neutral frames.

F1/Start or the HUD button opens bounded settings: independent camera X/Y
inversion (both true by default, image right/down), independent actor/camera
deadzones and sensitivities, reset and close. Common engine-free v1 settings
codec is shared with the lab, but WORLD stores only
`user://world_diagnostic_input_v1.json`, never the lab file. Isolated automated
restart tests do not overwrite ordinary preferences.
Modal digital navigation is single-press edge driven, not render/event-rate
repeat; held keys, D-pad, echo and unrelated mouse events do not advance it.
Focused numeric editors retain native digit/edit/Enter/Tab input. Semantic
navigation is consumed once, while world movement and Follow remain stopped.

Paint is terrain → diagnostic facades → five sorted statics → actor → guides
→ HUD. The persistent label says free diagnostic movement, collision not
evaluated and original parity NOT VERIFIED, including loading/failure/modal.
Shared `src/Arcanum.Input` owns project input/Follow/Vec2; historical
`GamepadLab.Core` names and type forwarding are compatibility only. The viewer
does not reference the experiment assembly, targeting or obstacle algorithms.

WORLD self-tests exercise the actual integrated app with public synthetic
pixels, viewport-dispatched modal/numeric events and complete-level
**SIMULATION** lifecycle frames, including incomplete-observation recovery.
Engine-free wire fixtures execute the production integrated WORLD load with
real DAT/ART/sector/static-chain parsers under a narrow identity authenticator:
per-domain/union/exact-byte retention limits, static-to-actor and partial-actor
cancellation, all-role post-load mutation, stale success/failure, serialized
disposal and exit drain. These are not original payload or physical-device
observations. Original graphical
native checks separately authenticate immutable module/terrain/actor/prototype
roles, terrain 4096/3033/1063 with zero issues and normalization 457/86,
static census 317/5/312, all 184 actor frames, motion/selection/readback,
requested=actual 1280x800,1440x900,800x600 viewports and texture disposal.
Headless checks are structural, not pixels. Source, exported original checks,
isolated settings restart and later ordinary unchanged-artifact observation
are separate evidence. Physical Xbox/Bluetooth comfort and original placement,
movement, occlusion/collision parity remain NOT READY/NOT VERIFIED; historical
lab/five-static owner observations do not verify this integration.

After source corrections and a clean committed candidate, use the closed
WORLD endpoint (not the historical manual recipe below):

    ARCANUM_LOCAL_HOME=OWNED_CONFIG ARCANUM_LOCAL_ROOT_ID=WORLD_ROOT_ID \
      ARCANUM_TERRAIN_EXPORT_TASK=ARC-WORLD-001 \
      tools/terrain-viewer/Godot/tools/validate-macos.sh

Configure WORLD separately at ignored `artifacts/world-builds`; keep legacy
roots/pointers. Use effective TMPDIR and a verified same-tool complete payload
estimate per [toolchain](../workflow/toolchain.md). The endpoint leases one
final head through gates/export/codesign/observation freeze/v2 finalization and
success-only registration. Select `WORLD_ROOT_ID:ARC-WORLD-001:<head>` and run
`tools/arcanum-local launch terrain --mode world-diagnostic` for ordinary use.
Default mode remains terrain-only. No test/capture/input/fullscreen automation
belongs in ordinary launch. Later observation logs/digest bindings stay outside
sealed evidence; never edit a finalized package or rerun an unchanged endpoint.

## Verification and evidence status

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
      --path tools/terrain-viewer/Godot --export-debug "macOS debug" "$BUILD_ROOT/ArcanumTerrainViewer.app"
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
