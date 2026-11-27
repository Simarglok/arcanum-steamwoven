# Crash Site exterior viewer (ARC-RENDER-007)

## Boundary and readiness

The explicit `area` mode consumes the immutable
[DATA-009 area snapshot and resource provider](../world/area-loader.md).
It is an exterior diagnostic renderer, not a new map parser, game runtime or
traversal authority. Historical `terrain-only`, `combined`, `actor-diagnostic`
and `world-diagnostic` modes retain their narrower contracts.

Source completeness, visible submission, graphical checks, exported-artifact
integrity, ordinary observation and independent acceptance are separate gates.
A `SOURCE READY` HUD, synthetic test, export exit or registered artifact does not
establish all of them. Original-runtime visual, placement and movement parity
remain NOT_VERIFIED. Physical controller/Bluetooth and QHD comfort are separate
observations, not software-test results.

Day/night ambient lighting over this mode (session game clock that flows by itself and
pauses only while the Esc menu is open, not for the palette or a dialog (ARC-WORLD-010, ARC-DEV-126), hourly scheme, indoor/outdoor
channels; diagnostic palette actions *Game time +1 hour* and *Ambient lighting on/off*) is the separate
[ARC-RENDER-010 component](area-lighting.md). The source-pixel gates below observe
unlit pixels; the native smoke then runs its own lit-pixel phase.

The selected FILE-effective profile has nine core sectors, X1451..1453 and
Y1289..1291. Its half-open world-cell domain is
`[92864,93056) × [82496,82688)`. It contains:

| Source domain | Records |
| --- | ---: |
| Core terrain | 36,864 (10,347 tiles, 26,517 facades) |
| Core statics | 426 |
| Core mobiles | 88 (2 containers, 52 items, 34 NPCs) |
| Inventory-insertion-refused core bodies | 15 |
| Eligible body submissions for this profile | 499 |
| Context terrain | 45,056, not drawn |
| Context sector coordinates | 11 present, 5 absent |

Context mobiles, outside mobiles, the full sector survey and editor/normal-game
correspondences remain coverage metadata. They are not additional approved draw
or movement domains. Resource descriptor counts are not occurrence counts:
many commands can share one decoded frame. Missing context is not manufactured
terrain or a collision border.

Core roofs, lights, wall/portal object types and required active composites are
absent in this authenticated profile. Their census is explicit; this does not
implement general roofs, walls, portals, lighting, shadows, underlays or overlays.
Unexpected required unsupported source states must fail, not become OutsideScope
or a placeholder. No AI, conversation, item pickup, door, combat, quest, time or
cave transition executes.

## One chart; separate visual and motion domains

`AreaScenePlan` uses the retained WORLD start-sector chart origin `(92928,82560)`.
For original world coordinates, subtract that origin once, then use checked
`P(dx,dy) = (40*(dy-dx), 20*(dx+dy))`. All sectors share it; local record indexes
are never scene identities. Terrain follows the existing diagnostic footprint:
X is projected X + 1; tiles are at most 78×40 and facades 80×40, cropped to the
selected frame. There is no sector-edge clip or seam-specific nudge.

An area body uses projected cell corner plus its DATA-selected object offsets
and `(40,20)`, minus the selected signed hotspot. Serialized scale is checked
and truncated toward zero before optional shrunk division by two. ART frame
offsets remain metadata, not another position correction. Descriptor mirror XOR
serialized blit-X and independent blit-Y reflect sampling within that rectangle.
They do not change map-cell depth or grant a different footprint. This is the
admitted diagnostic placement policy, not proof of retail placement for every
mirrored ART getter or cached render branch.

The controlled HumanFixed bridge remains a separate session actor, not one of
DATA-009's NPC/PC occurrences. Its authoritative grid position, event-derived
animation and existing pure world foot projection are unchanged. Camera motion
and asset dimensions do not feed back into simulation. A production
`area` load now admits motion across all nine core sectors with the
[whole-core T0 step assessment](../world/area-step-assessment.md), built from the
same strict Area publication plus `--area-effects` (`arcanum3.dat`) and
`--area-patch` (`modules/Arcanum.PATCH0`). `AreaGridSession` keeps the chart's
base-sector-relative cells (spawn 6,1; the western and northern core sectors have
negative cells) and maps them to map cells for Data. Unknown, Blocked and
outside-core requests remain denied; the core edge behaves like a map edge.
The area mode accepts only whole-core evidence bound to the publication's
generation; the bounded single-sector snapshot is WORLD-only. The synthetic
native AREA probes below use structural whole-core evidence over their strict
owner fixture. Being able to inspect the whole exterior, including the context
ring, is not permission to walk outside the core.

Fit uses the complete admitted scene envelope, not one sector or only currently
visible sprites. The existing minimum zoom is retained. If that limit prevents
whole-envelope containment, the HUD reports `Fit limited by minimum zoom`; pan
still permits inspection. Fit is session-only and does not overwrite preferred
zoom. WORLD camera/input/zoom/settings policies remain those in
[terrain-viewer.md](terrain-viewer.md#integrated-world-diagnostic-policy).

## Composition and visibility

Terrain tile and facade phases precede bodies. Flat scenery with UNDER_ALL
precedes other flat bodies; upright bodies and the controlled actor are composed
together, not as an unconditional actor foreground pass. Body traversal is global
map Y, then map X, then same-cell second/first offset ranks. Those ranks retain
the center-shifted inverse-offset convention with two truncation points. Texture
height, hotspot, camera pan and zoom do not enter this ordering.

Equal-rank deterministic identity tie-breaks are project policy, not recovered
retail insertion order. The historical five-only
[static scenery diagnostic](../research/scenery-placement.md)'s
`(anchorY,anchorX,sourceIndex)` is not the area's universal order.

The normal diagnostic observer does not detect invisibility. Inventory insertion
refusal (`0x1000`), serialized destroyed/off/dontdraw (`0x103`) and invisible
(`0x200`) dispositions retain keyed reasons. Suppressed records are not silently
lost resource records. Unknown mandatory state, unsupported blend/translucency,
or missing eligible body appearance/position fails planning. Generic hidden-body
behavior is not permission to discard independently required other layers.

Viewport culling uses destination rectangles, including canopy extents when their
anchors are offscreen. Source coverage remains independent of visible/cull state.
Missing resident visible textures fail; culled records do not become missing.
[Exact nearest sampling](area-nearest-sampling.md) is a project-defined pixel
rule, not a claim that every proprietary renderer implements the same rule.

Terrain is drawn as pre-composed 512 px world chunks when the plan admits it:
every terrain command 1:1, alpha 255, not additive, not Y-reflected, in phase 0/1
before every body, and sampling only fully transparent or opaque source texels.
A chunk texel is the last terrain command in plan order whose source texel there is
opaque, mirrored within its crop, so under the nearest rule a chunk shows exactly
the pixels of its per-command terrain draws. At zoom 1/2, 1/4 and 1/8 with an integer
raster origin a decimated chunk keeps only the texels that zoom samples. If the
plan does not admit chunks, or the visible chunks would exceed 192 MiB or 1,024
textures, terrain is drawn per command as before. Visibility, culling counts,
coverage and pixel observation still use the per-command list. The
proof of exactness: for a 1:1 item at integer destination `d` and zoom `z` the nearest rule selects source
index `floor((s + 1/2 - o) / z) - d` for screen pixel `s`, so every 1:1 item samples the same world texel `W = floor((s + 1/2 - o) / z)` and owns the
pixel exactly when `W` lies in its destination; with only binary alpha and painter order the pixel shows the last terrain command whose texel at `W`
is opaque, which is what a chunk composed on the CPU in the same order holds at `W`, and adjacent chunks partition the world texels, so seams add or
drop nothing. At zoom `2^-k` with an integer origin `W` is congruent to `2^(k-1)` modulo `2^k` for every pixel, whatever the pan, so a decimated
chunk that keeps only those texels, drawn at `2^k` times its size, selects exactly them; other (continuous) zooms need the full chunk.

## Resource ownership and reports

Each load has one DATA owner, one strict snapshot load and one frame batch that
copies each unique required frame once; nothing is published unless the batch
completes its final source authentication. Explicit cumulative DATA usage is
pre-admitted. Reload uses a new owner,
not refunded read/decode counters on an exhausted owner.

The CPU repository is limited to 2,444 frames and 81,853,096 packed RGBA bytes.
Visible GPU uploads have the same area count/byte envelope; the controlled actor
retains its separate closed catalogue. Keys include source entry/payload, view,
policy, physical selection, effective palette and output identity. Occurrences
remain separate commands even when their frame is shared. Frames that leave the
view stay uploaded within a least-recently-used allowance of 512 frames and 32 MiB
beyond the visible set; older ones are evicted and reuploaded from retained CPU
copies without another DATA decode. A different frame repository (a new load)
releases every area frame texture first, so equal keys never reuse another load's
pixels.
Integer sampling metadata has a separate 16 MiB cap. Terrain chunk textures have
their own 192 MiB / 1,024-texture cap, with chunks of the current view never evicted
and older chunks evicted least recently used; terrain frames are uploaded only when
terrain is drawn per command. Under lighting a chunk whose terrain is on more than one
channel (indoor and outdoor tiles) is drawn as one item and one texture per channel
(ARC-RENDER-3438): a channel's texture holds the texels whose winning command is on that
channel and is transparent elsewhere, so no two share a texel and each is tinted by its
channel's ambient, with the pixels of the per-command draw. These textures count against
the same cap; another channel table drops them. The whole view falls back to per-command
terrain only when the plan admits no chunks or the view's chunk textures exceed the cap.
Missing textures of a view are composed in parallel (batches of 32, byte-identical to
composing them one by one) before upload. The visible set is
selected through a coarse 512 px grid over the non-upright commands
(`AreaVisibilityIndex`), which only narrows the candidates: the exact intersection
test and the plan order are unchanged, and a view spanning more grid cells (empty ones
included) than half the occupied cells scans every command. A second grid of the same
kind over the upright bodies keeps their draw order and orders only the bodies it
names for the view, with the moved commands and the actor placed among them as in the
whole list (ARC-RENDER-3417). These are admission policies, not measured
process/GPU memory or load-speed guarantees.

The visible set is rebuilt once a frame (ARC-RENDER-3417). Publishing a new selection
of the same plan, frame repository and texture cache, or changing the hover highlight
or ring, only marks the view; the canvas rebuilds it in its own update, which follows
the viewer's update and therefore the frame's camera. A publication that comes after
the canvas's update is rebuilt later in the same frame. A read of the submission
after a publication rebuilds first, unless another input of the view changed since;
that input may belong to a plan not yet published, so the read then answers with the
last submission. Another plan, repository or cache is built at once, as is a
switch of the visual profile.

The HUD separates core/context, terrain/statics/core mobiles, body submissions,
N/A, visible/culled, CPU/GPU counts and byte estimates, measured load duration,
generation, movement limitations and absent unsupported features. Private
coverage reports compare source occurrences, selected appearances, eligibility,
commands and observed samples against an independent original oracle. Original
frame proofs, screenshots, paths and detailed reports stay ignored/local and
must not enter Git or an app payload.

In an interactive run the viewer caps its frame rate (`FramePacingPolicy`): at
the current display's refresh rate while input is held or recent (0.5 s), the
camera moves or the WORLD actor walks or turns, and at 60 FPS (or the display rate, if lower) otherwise. The engine
cap is corrected once per second from the measured rate, within 1–4 times the
target, because Godot's limiter alone undershoots on macOS; only a cap that met
its target is remembered for the next time that target applies, so a frame-bound
pan does not leave later light views uncapped. The per-frame WORLD
status line is rebuilt at most ten times per second while active and once per
second while idle; event-driven refreshes stay
immediate. Self-tests and the native smoke run uncapped with per-frame status.
These are interactive comfort policies, not frame-time guarantees.

## Independent synthetic checks

`AreaCompositionTests` feeds independently authored raw state/position/visual
inputs into the production planner. `TerrainViewerAreaCompositionTest` sends
those plans through the real canvas/cache/shader and reads a 480×400 SubViewport.
Its literal matrix covers same-cell residual ranks and ties, cross-cell Y/X
ordering that disagrees with projected anchor Y, size/hotspot/scale/shrunk state,
signed hotspots, flat/UNDER_ALL and serialized suppression reasons. Actor-only,
scenery-only, full and restored views establish nonvacuous overlap pixels.
Separate asymmetric markers exercise descriptor mirror and both serialized flip
axes; offscreen-anchor canopies exercise rectangle culling. Cases include .5/1/2
zoom, repeated pan and exact texture created/disposed ownership.

The numeric fixture does not authenticate original FILE/DAT/ART inputs or replace
the required owner/coordinator adjacent-sector fixtures, production area input/UI
matrix, measured full-area sweep/reload/resource checks, original graphical gates
or final exported/ordinary observation. Headless structural results cannot claim
pixel readback. See [toolchain](../workflow/toolchain.md) and
[local development](../workflow/local-development.md) for endpoint, lease,
capacity, immutable-head and evidence-retention requirements.

`AreaOwnerFixture` supplies independently authored FILE/DAT/MES/ART bytes for
four cardinally connected sectors at an X seam, Y seam and shared corner. The
real `AreaDataOwner.LoadStrict`, planner and production frame-copy stage retain
all 16,384 terrain occurrences and five distinct bodies, including repeated
null IDs and identical local source indexes across sectors. Literal world
rectangles, signed object offsets/hotspots and ART-offset sentinels check that
the decoder and planner use one chart. Two shared frames serve all commands.

`AreaOwnerRepositoryTests` checks exact repository count/byte admission and
cap-plus-one refusal before acquisition, cancellation before and after copies,
resource mutation/read failure, descriptor completeness, foreign snapshots,
lease release and transferred stream disposal. Tests can only lower the
production repository limits. Provider allocation/read charges are not refunded
to make a failed copy succeed.

`TerrainViewerAreaOwnerTest` renders that strict structural snapshot through the
real canvas/cache, with literal seam/corner pixel samples, body-hidden controls,
repeat pan/zoom, offscreen-anchor canopy culling and texture disposal checks.
Rendering after owner disposal proves that camera pans use retained CPU copies,
not another provider decode. This structural fixture is not original-data
authentication, the complete production coordinator/controlled-actor load, an
AREA input/UI matrix, or full-area measured resource/reload acceptance.

`AreaOwnerCoordinatorTests` now exercises the strict snapshot/planner/repository
phase shared with `AreaLoadOperation.Execute` inside the serialized coordinator.
It uses a fresh owned structural source for each generation, proves disposal
before the next operation is created, rejects an obsolete completed generation's
publication, and cancels a blocked real source read without returning a partial
area. Its legacy result envelope is test transport only: it does not authenticate
the original traversal snapshot or controlled HumanFixed catalogue.

The opt-in `TerrainViewerAreaOwnerSeamTest` uses a second wire variant with four
mirrored corner cells and an asymmetric tile. Columns 78/79 contain a magenta
sentinel which must be cropped out, including after reflection. The independent
standard-library `tools/area_owner_seam_oracle.py` enumerates the full authored
128×128 wire grid, not submitted command rectangles. Exact rational pixel
centers and literal global cell ordering freeze nine views/297 expected samples
before native readback at .5/1/2 zoom and three pans. Both halves of each mirrored
cell are observed; the whole 480×400 readback is checked for sentinel leakage.
This is an owner-path crop/seam proof, not another nearest/backend parity gate.

To reproduce it, first build the viewer with the pinned toolchain, then generate
the oracle into an owned ignored directory and launch the native synthetic probe:

```sh
python3 -B tools/terrain-viewer/Godot/tools/area_owner_seam_oracle.py --output "$OWNED_EVIDENCE/seams.json"
ARC_AREA_OWNER_SEAM_PROBE=1 ARC_AREA_SEAM_ORACLE="$OWNED_EVIDENCE/seams.json" \
  godot_canonical --path tools/terrain-viewer/Godot -- --self-test
```

The oracle output is create-once and its frozen digest is checked by the probe.
Keep it outside sealed payloads. Original role/path authentication, the entire
AREA owner/coordinator/actor/traversal integration, production AREA input/UI and
full resource/export/ordinary acceptance remain separate required checks.

## Authenticated coordinator binding

`AreaOriginalCoordinatorTests` is an opt-in real-data check of the default
`TerrainLoadCoordinator` factory and complete `AreaLoadOperation.Execute` path.
It loads two fresh serialized generations, checks the full original area census,
distinct retained frame arrays, the HumanFixed catalogue and the authenticated
whole-core step evidence and epoch, then refuses obsolete publication. Literal
Open, admitted Blocked, a former bounded-zone edge step and an outside-core
assessment keep visual area coverage separate from locomotion permission.
Negative controls reject a stale generation and another generation's evidence at
the production step binding boundary. This is not a synthetic owner envelope or a replacement for
the production Godot movement/input/UI matrix.

The production loader binds traversal module/resource identities and the
base-sector chart to the same area generation before returning a result.
Cancellation is checked after original traversal validation and before result
publication. The Godot area boundary also requires HumanFixed and the matching
traversal epoch. Private coverage reports retain the actor catalogue count,
decoded bytes, current state and traversal identity alongside area coverage;
their full original occurrence/appearance comparison remains independent of
these binding checks.

To run the coordinator check, supply user-owned read-only paths through
`ARC_AREA_MODULE`, `ARC_AREA_RESOURCE2`, `ARC_AREA_RESOURCE1`, `ARC_AREA_MOBILE`,
`ARC_AREA_PROTOTYPES`, `ARC_AREA_EFFECTS` and `ARC_AREA_PATCH`, set `ARC_AREA_ORIGINAL=1`, then use the pinned .NET
toolchain:

```sh
dotnet test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj \
  --no-restore --filter FullyQualifiedName~AreaOriginalCoordinatorTests
```

Without opt-in the test is inconclusive, not an authenticated PASS; missing roles
after opt-in fail. Keep detailed original reports and diagnostic dumps private,
outside sealed artifacts. Two complete original loads took about 50 seconds on the development Mac;
the test bounds each awaited load independently. Successful coordinator/source
checks do not establish the full AREA software-input, resource/reload, final
export or ordinary-launch gates, nor original-runtime parity.

## Production AREA input, camera and resources

The opt-in native `ARC_AREA_GRID_PROBE` publishes the strict
`AreaOwnerFixture(motionBodies:true)` snapshot through the production app with
HumanFixed and structural whole-core step evidence built from that same
snapshot (`AreaOwnerFixture.Steps`: the body at cell 5,1 blocks; the canopy at
6,1 carries `OF_NO_BLOCK`; a second evidence set without the tile-name 300 anchor
leaves every tile Unknown). Its literal script
drives Godot key callbacks and complete SIMULATION frames through held renewal,
turn at arrival, return reinput, Blocked/Unknown/Unsupported denials and three
atomic fault controls (queue capacity, replay capacity, late callback). Fault
disposal clears the scene, queue and GPU ownership and reports area.
Unsupported movement stops at the admitted core edge: after (6,1)→(6,0) the step
to (6,-1) lies in an absent fixture sector. Showing the area grants no cell.

Moving overlap witnesses come from the independent standard-library
`tools/area_motion_oracle.py`, which restates the projection, body center,
floor/half-even actor residual, rank and order rules on its own. The fixture
canopy at cell 6,1 has object offsets (-74,40) and ART hotspot (-20,80) so that
its opaque left half covers the actor in cells 6 and 7: destination
(-214,120,128,160), ranks (97,-17). The actor anchor is the diamond centre
(projection plus residual plus `(40,20)`, ARC-RENDER-011). The actor is behind it while its floor map cell is 6 (idle, outbound
6.25 and mirrored return 6.75) and ahead in cell 7 (idle and outbound 7.25).
Each of the five views is read back at zoom 1, 2 and 3 with actor-only and
canopy-only negative controls; actual rectangles are logged before asserting.
The matrix owns its simulated focus: native window focus belongs to the
separate WORLD main-window probe, not to whichever application holds the desktop.

Next it runs the ARC-WORLD-007 [click-to-move](../world/click-route-runtime.md)
route matrix on the same fixture: clicks through the production `_UnhandledInput`
seam and `SubViewport.PushInput` (a HUD click is consumed by GUI), a detour around
the 5,1 blocker, a new click mid-step, the same goal kept, a stick takeover, a click
just before a stick release, the
palette stop with the started step ending at its cell centre, and an occupied goal
(`ARC-WORLD-007 AREA route synthetic PASS`).

The same probe then runs the AREA camera matrix: R restores the stored preferred
zoom in Follow; Fit centers the complete envelope, reports `Fit limited by
minimum zoom` at 0.125 and leaves the preference unchanged; keyboard camera pans
through the production reducer reach all four envelope edges without moving the
actor or its cell; a wheel step persists, and a fresh initialization and
republication use it.

```sh
python3 -B tools/terrain-viewer/Godot/tools/area_motion_oracle.py --output "$OWNED/motion.json"
ARC_AREA_GRID_PROBE=1 ARC_AREA_MOTION_ORACLE="$OWNED/motion.json" \
  WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH="$OWNED/input.json" \
  WORLD_DIAGNOSTIC_ZOOM_TEST_PATH="$OWNED/zoom.json" \
  godot_canonical --path tools/terrain-viewer/Godot -- --self-test
```

The probe requires the oracle output, pins its digest and checks every literal
rectangle, witness and winner against it. The `ARC-RENDER-007` export endpoint
generates the motion, seam and nearest oracles first and runs the grid, seam
and nearest probes as receipt-bound source and exported gates
(`{source,exported}-area-{grid,seams,nearest}`), each with owned preferences.

The endpoint reads every original role from the configured `ARCANUM_LOCAL_HOME`,
which therefore needs the seven area roles (module, terrain resource, actor
resource, prototypes, `areaMobile`, `areaEffects`, `areaPatch`) plus `sourceId`,
and an ignored artifact root registered with `arcanum-local configure
add-artifact-root` and named by `ARCANUM_LOCAL_ROOT_ID` (for example `world`).
It also needs three private references in the environment: `ARC_AREA_ORACLE`
(the independent numeric oracle of the [area loader](../world/area-loader.md),
pinned by digest), `ARC_AREA_PRIVATE_EVIDENCE` (an ignored directory under the
candidate's `local/`) and `RENDER006_HUMAN_ORACLE` (the private human reference
used by the inherited human and grid gates). Without the last one
`source-human-original` fails first, at `private-reference-admission`. A failed
same-head run cannot be retried: the endpoint keeps the partial candidate
intact, so a corrected run needs a new committed head.

After its sampled-pixel stage, the original `area` native smoke
measures resources on the published area. A zoom-1 sweep tiles the whole
envelope twice, first with terrain chunks (the default) and then with per-command
terrain; every one of the 37,363 commands must be submitted at least once per
sweep while visible GPU residency stays within the 2,444-frame/81,853,096-byte
admission caps, frames resident beyond the visible set stay within the retention
allowance, and chunks within their own cap. Both caches must exceed their
allowance and evict: chunks in the first sweep, frame textures (reuploaded from
the unchanged CPU repository) in the second. Two
overlapped production reloads follow: the cancelled first generation must not
publish, and the successor must show the same fresh-owner usage counters. After
the replaced generation the retired load result and its frame repository must
be unreachable (weak references after a forced collection), file descriptors
must not grow and post-GC managed growth must stay below half of the measured
repository (each side the least of three post-GC samples a frame apart, since
ARC-RENDER-018). The smoke releases its own result reference before this stage.
Resident size is recorded, not bounded, because freed runtime/driver pages need
not return to the system. One full original load currently takes
about 22 seconds in a Debug source run and 12–13 seconds in the release export on
the development Mac (see the
the 2026-10 performance review, deleted; its figures are kept here); one
replacement is measured, well within the 900-second validator bound. Process
figures are measured observations, not a memory guarantee. `area_validate.py` keeps only the
scalar summary in the safe receipt; the raw report stays in private evidence.

## Source attribution and unknowns

The specification comes from a reading of the original's functions at these
addresses, evidence level F0 (not checked in the executable) unless the
verification ledger lists them:

- sector rectangle from a location rectangle (0x4D0090) and the object draw
  (0x43B390): global cell traversal;
- object list insertion helpers (0x4F20A0, 0x4F2230) and a location helper
  (0x4B93F0): flat/upright, UNDER_ALL and local offset insertion ranks;
- the object draw (0x43B390) and the object rectangle (0x43DFD0): render phases
  and normal body position; inventory insertion is the object list insert
  (0x4F2020);
- serialized flags and appearance census are separately recorded in the
  [inventory](../research/area-inventory.md) and
  [area contract](../world/area-contract.md).

This is one specification lineage, not independent original execution. Code and
public synthetic fixtures are independently authored. General cached rendering, mirrored-getter
compatibility beyond the admitted diagnostic policy, retail ties, shadows,
lighting and original-runtime parity remain unverified or unsupported. Neither
source attribution nor successful synthetic pixels removes those boundaries.
