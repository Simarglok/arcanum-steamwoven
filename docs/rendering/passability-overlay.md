# Passability overlay (ARC-RENDER-008)

**Status:** delivered in the Terrain Viewer WORLD modes (`world-diagnostic`,
`crash-site-area`). It is a read-only **diagnostic display of the movement assessor**:
not a collision system, a permit, navigation or original-executable parity.

## Toggle and state

- Palette checkbox **Show passability (read-only)** (palette: Ctrl+Shift+F9, HUD
  button or gamepad Start; see [diagnostic palette](../input/diagnostic-palette.md)).
  The checkbox is reachable by mouse, keyboard (Up/Down, Enter/Space) and the
  selected controller (D-pad/stick, A). One toggle per press; echo/held input does not
  repeat. No other key toggles it: plain F1/F3 stay original game keys.
- **OFF at launch.** Session state only (kept across Reset scene and Reload, not saved).
- Without a ready movement session the row reads "unavailable: no movement authority"
  and is not navigable; the HUD shows `OVERLAY_UNAVAILABLE`.
- Toggling never quarantines input or changes actor, movement session, camera, Follow,
  zoom or preferences.

## Authority and classification

The overlay calls exactly the assessor the movement session uses:
`AreaGridSession.Assess` (the current map at T0 in its own chart: the whole Crash Site core,
profile `CRASH_SITE_CORE_T0_V1`, or after ARC-WORLD-009 the cave)
or `DiagnosticGridSession.Assess` (bounded WORLD003 zone x4..8, y0..3 of sector
1452,1290, profile `BASE_SECTOR_1452_1290_ADJACENT_V1`), with the session's own
snapshot, actor (`HUMAN_FIXED_DIAGNOSTIC`) and declared context. Each verdict keeps
the assessor's reason code. Nothing is inferred from sprites, alpha masks, visibility,
ART or raw terrain flags.

For a cell C the overlay asks all **eight incoming directed steps** (from each
neighbour into C, in that neighbour's direction). Steps the assessor answers
`Unsupported` (source, destination or shoulder outside coverage; context mismatch)
are not admitted.

| Cell class | Rule | Drawn as |
| --- | --- | --- |
| Blocked | **every** admitted incoming step is `Supported/Blocked`, and at least one of them is map-cardinal | translucent red fill + red outline |
| Open | at least one admitted incoming step is `Supported/Open` | no fill |
| Unknown | no admitted step is Open, and one is `Unknown` or every admitted one is a Blocked map diagonal | gray hatch + gray outline |
| Outside | no admitted incoming step (outside the authority's coverage) | gray hatch |

Directed marks keep edge-only facts: a `Blocked` step into a cell that is not wholly
blocked is a **red boundary and inward arrow** on the shared edge (map-cardinal step)
or at the shared corner (map-diagonal step); an `Unknown` step into an Open cell is a
gray arrow. The reverse direction is a different step and appears only on the other
cell, so asymmetric pairs stay visible. A red fill needs the strict proof of all
admitted incoming directions **and** a map-cardinal one: in both assessors a
map-cardinal step is Blocked only by the destination cell itself (AREA
`DESTINATION_TERRAIN_BLOCKED`/`DESTINATION_OBJECT_BLOCKER`, WORLD003 destination terrain or
`ADMITTED_DESTINATION_SCENERY_BLOCKER`), while a map-diagonal step can also be blocked by a
shoulder (`SHOULDER_OBJECT_BLOCKER`, `ADMITTED_ADJACENT_SCENERY_BLOCKER`). Diagonal-only
blocks therefore stay directed marks on an Unknown cell. Neither Unknown nor Outside is
ever drawn as Open or red.

Example (original data, both modes): `(6,1)->(5,1)` is Blocked by the admitted scenery
at `(5,1)` (WORLD: `ADMITTED_DESTINATION_SCENERY_BLOCKER`, AREA:
`DESTINATION_OBJECT_BLOCKER`), so `(5,1)` is filled; the reverse `(5,1)->(6,1)` is
Open; the diagonal `(6,1)->(5,0)` is an edge-only blocker (shoulder `(5,1)`).

## Geometry

Cells are drawn in the canvas' own chart space with the shared projection: a cell's
top vertex is `(originX + 40*(Y-X), 20*(Y+X))`, its diamond 80x40, its centre the actor's
projected anchor. On the AREA chart `originX` is 40, so each diamond is the terrain tile
the actor stands on ([ARC-RENDER-011](terrain-viewer.md#actor-anchor-and-wall-fade-arc-render-011));
`world-diagnostic` keeps 0, as its actor does. The overlay layer is a child of the scene canvas (same transform, no
second scaling) and draws above terrain, scenery and actor; the HUD is a separate layer
above it. Pan, Fit, zoom and resize change only what is visible, never a cell's class.

## Coverage, cache and limits

- Only visible cells are assessed, at most 1,024 new cells per frame, in a fixed
  row-major order. Results are cached per authority identity (profile, evidence digest,
  epoch, context) and movement session: a new session (Reset scene, Reload, republish),
  snapshot, epoch or context binds a new overlay, and a failure or scene clear drops every
  cell and the last-step line. Cached cells are never reassessed; a fully assessed view is
  not rescanned until it moves.
- Above 20,000 visible cells (e.g. AREA Fit at minimum zoom) nothing is drawn and the
  HUD says `OVERLAY_ZOOM_IN`.
- AREA: cells outside the current map's admitted sectors (exterior: the nine core sectors;
  the drawn context ring) are Outside
  without a request. WORLD: every cell of the sector outside the bounded zone is
  answered Unsupported by the assessor (`ZONE_UNSUPPORTED`).
- OFF: no overlay instance exists, no assessment and no drawing happen.
- An assessor exception is `OVERLAY_AUTHORITY_FAILED`: nothing is drawn, nothing is
  reported as Unknown.

## HUD

While ON the HUD shows profile, actor, coverage boundary, visible counts (Blocked,
Open, Unknown, outside, blocked/unknown edges), cached cells and assessment count, the
last attempted step (`from->to direction coverage/disposition reason`) and the legend.

## Evidence

- `tests/Arcanum.TerrainViewer.Tests/PassabilityOverlayTests.cs`: literal verdict-table
  cases (Open, fill, edge-only and asymmetric, Unknown, Outside/context mismatch, chart
  boundary, missing neighbour, budget/cache, authority failure, replacement) and the
  production AREA assessor on the public strict fixture.
- Godot self-test `ARC-RENDER-008 overlay matrix PASS` (WORLD synthetic) and the AREA
  grid probe (`mode=crash-site-area`): OFF at launch and zero assessment while OFF; F3
  inert; click/controller/keyboard single toggles; display-only invariants; literal
  blocked/open/outside classes; rasters (graphical) for red fill (exact blend), untouched
  Open cell, gray hatch and a directed red mark at zoom 0.5/1/2 and 1280x800/2560x1440,
  HUD pixels unchanged; Fit keeps classes; equal movement traces ON vs OFF;
  `ARC-RENDER-008 overlay invalidation PASS` (republish rebinds, failure drops cells).
- Original data, inside the WORLD and AREA native observations:
  `ARC-RENDER-008 original overlay PASS` with the matched Open `(6,1)->(7,1)`, Blocked
  `(6,1)->(5,1)`, Open reverse, a positive directional Blocked and an Outside cell; the
  native runners require this marker. No original bytes or pixels are stored.
- Export: literal task `ARC-RENDER-008` with the ARC-RENDER-007 gate set; `./arcanum
  area` prefers its build.

**NOT VERIFIED:** visual or collision parity with the original executable; physical
controller comfort. Coverage stays the movement authority's (whole core at T0 or the
bounded WORLD003 zone); full-area traversal beyond it needs separate work.

## Owner checklist

1. `./arcanum area`, open the palette, tick **Show passability**: the HUD says ON.
2. Pan, zoom 1.00/2.00 and Fit: classes stay in place; at minimum zoom the HUD asks to zoom in.
3. West of the spawn the scenery cell `(5,1)` is red; a red arrow marks the diagonal into `(5,0)`.
4. Beyond the core edge the cells are gray-hatched.
5. Walking with the stick behaves exactly as with the overlay off.
