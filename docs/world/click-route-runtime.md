# Click-to-move route (ARC-WORLD-007)

Status: **Implemented**, stage 1 (Godot-free core) and stage 2 (LMB in the
`crash-site-area` viewer). The core plans a bounded route to a clicked cell over the
caller's step evidence and turns it into ordinary
[movement runtime](grid-movement-runtime.md) intents; the viewer picks the clicked
ground cell and drives the actor there ([viewer binding](#viewer-binding-stage-2)).
`world-diagnostic` has no click route. Original pathfinder and runtime parity are
**NOT_VERIFIED**.

Requirements: [FR-NAV-001](../requirements/world-travel-runtime.md#fr-nav-001)
(click is a route goal), [FR-NAV-002](../requirements/world-travel-runtime.md#fr-nav-002)
(the route uses the step evidence the actual step uses),
[FR-NAV-004](../requirements/world-travel-runtime.md#fr-nav-004) (segment cap is not
a goal limit) and KM-03 in [original keyboard and mouse](../requirements/original-keyboard-mouse.md).

## Provenance

Project policy shaped by the bounded source reading in the
[pathfinding study](../input/arcanum-pathfinding-study.md) of the pinned CE commit
`a7ff41b300ef712f0e7d088183a3d08957110cdd` (`path.c` `path_make`, the straight
attempt and the 64×64 local search; `location.c` `location_rot`). The structure and
limits below follow that reading; the code is independently written, and no CE code
or table layout was copied or translated. Tie-breaking, the open set, the turn cost
model and repeated segmenting are project choices. CE's special path flags (doors,
windows, traps, fire, light, `0x1000`) are not modelled; the step source decides
every step.

## Planner: `RoutePlanner.Plan`

`Arcanum.Simulation.Navigation` (no Data, Input or Godot dependency):

```csharp
RoutePlan RoutePlanner.Plan(IRouteStepSource source, CellCoord from, CellCoord target, RouteLimits? limits = null);
interface IRouteStepSource { bool Contains(CellCoord cell); RouteStepVerdict Assess(CellCoord from, NeighborDirection direction); }
```

1. **Refusals, never searched:** source or target outside `Contains`
   (`ROUTE_SOURCE_OUTSIDE_AREA`, `ROUTE_TARGET_OUTSIDE_AREA`); more than
   `AxisDistance` (32) cells on either axis (`ROUTE_TARGET_OUT_OF_RANGE`). The same
   cell is `AlreadyAtTarget`.
2. **Straight attempt:** from each cell step toward the target, diagonally unless the
   minor remaining delta is under half of the major one (`RoutePlanner.Toward`).
   Every step must be `Open`; the first other verdict ends the attempt with no
   partial route (`ROUTE_DIRECT` on success).
3. **Local search:** an eight-direction best-first search in a window of
   `2 × AxisDistance` (64) cells per axis, centred on the source/target box. Step cost
   10, +1 when the direction changes, estimate 10 × Chebyshev distance, at most
   `Expansions` (4,096) expanded cells, deterministic tie-break by cell index
   (`ROUTE_LOCAL_SEARCH`). Cells outside the window are never asked about. The search
   is near-shortest: a turn costs a tenth of a step, so a route may be a few steps
   longer to avoid many turns, and with one entry per cell it is not exact under this
   cost. Nothing here claims original path quality.
4. **No route:** `ROUTE_EVIDENCE_INCOMPLETE` when an `Unknown` step was met (a route
   may exist through it), otherwise `ROUTE_WINDOW_EXHAUSTED` when the window edge cut
   the search (a route may exist outside it), otherwise `ROUTE_NOT_FOUND` (only
   Blocked/Unsupported steps closed it); `ROUTE_EXPANSION_LIMIT` when a lowered budget
   ran out. A goal that none of its eight adjacent steps can enter is answered from
   those steps alone (`ROUTE_NOT_FOUND`, or `ROUTE_EVIDENCE_INCOMPLETE` if one is
   Unknown). `ROUTE_COORDINATE_OVERFLOW` is a defensive refusal only.

Only `Open` is traversable; `Blocked`, `Unknown` and `Unsupported` are never promoted.
Each directed step is asked at most once per plan; the plan reports the counts of
assessments, expansions and Blocked/Unknown/Unsupported steps. `RoutePlan.Steps` is
the whole route, `Cells` the cells it enters and `Segment` its first
`SegmentSteps` (8) steps. `RouteLimits` values can be lowered, never raised.

## Follower: `RouteFollower`

The follower owns the goal, never the clock, evidence or movement state:

```csharp
bool SetTarget(CellCoord target, SimulationTime observedAt);   // false: same goal already followed
void Stop(SimulationTime observedAt, string reason = "ROUTE_STOPPED_BY_HOST");
RouteSubAdvance Next(MovementState state, SimulationTime target);   // Until + batches for the host
void Observe(MovementResult result);
RouteDrive Drive(MovementState state, SimulationTime target, Func<SimulationTime, IReadOnlyList<InputBatch>, MovementResult> advance);
```

- One click sets a goal; release has no meaning here (KM-03). The actor walks to the
  goal through `Segment`s, planning again from the cell it will stand on after each
  segment, so a goal farther than eight steps is reached in several segments
  (FR-NAV-004) instead of being cut off.
- Decisions happen only at a settled cell or at the exact end of the current step, for
  the cell the actor will stand on. The host advances to that boundary with the
  `SetActorIntent` input there (a Neutral first when a denied direction is still
  active), so consecutive steps chain without an idle gap and no input lands inside a
  step. A goal observed later than the advance target waits for a later advance.
- A new goal or `Stop` never cancels a started step: it takes effect at the next cell
  centre, so there is no jump. The same goal again is kept, not queued twice.
- The goal is reached when the actor is settled on it (`Arrived`, intent Neutral). A
  failed plan stops with the plan's reason; a step the runtime denies stops with
  `ROUTE_STEP_DENIED`, and the next advance clears the intent (possibly in the next
  `Drive` call). A route is never continued through a denial; a new click after it
  starts cleanly.
- While a goal is followed it owns movement and stops the actor on the goal even if
  another source set the same direction; otherwise the follower clears only an intent
  it issued itself, so a host may keep driving it while another input source moves the
  actor. Ownership is the receipt key of the follower's last input, not the intent
  value: after a stop, a stick that sets the same direction owns it. With no goal and
  no intent of its own the follower returns the host's target directly, so it never
  splits another source's advance at a step end. Observation times gate a settled actor only; a goal change during a step
  applies at that step's end. `Next` plans as it decides, so `LastPlan`,
  `PlannedSegments` and a plan failure are recorded even if the host drops that advance.
- `Drive` runs `Next`/`Observe` through the host's advance up to the frame target,
  at most 1,024 sub-advances per call, and returns every `MovementResult` in order for
  presentation.

## Crash Site core adapter

`AreaRouteSource` (Terrain Viewer Core) answers steps in `AreaGridSession` chart
cells by calling the same `AreaStepSnapshot.Assess` the session admits steps with:
Supported Open is `Open`, Supported Blocked is `Blocked`, `Unknown` and
`Unsupported` (the core edge, owner decision of 2026-10-15) stay distinct.
`Contains` is the nine admitted core sectors. A planned step and the step the
runtime receives therefore share one Data verdict.

## Viewer binding (stage 2)

`TerrainViewerWorld` binds the follower to the whole-core `AreaGridSession` only
(`crash-site-area`); each session (spawn, Reset scene, Reload) gets a fresh
`RouteFollower` over its own `AreaRouteSource`.

- **Click.** An LMB press that native GUI leaves unhandled (`_UnhandledInput`; the
  HUD panel and the palette consume their own clicks) sets the goal with the
  observation time of the click. It is ignored while the palette or a GUI control has
  focus, the window is unfocused, the neutral gate is closed or Reset/Reload is
  pending. Release, drag, double click and the other buttons have no route meaning;
  the same cell again keeps the goal (KM-03).
- **Picking.** `AreaCellPicking` maps the click through the canvas transform to chart
  space and picks the diamond of the drawn ground: cell (x,y) is centred at
  `(40(y-x)+40, 20(y+x)+20)`, the centre of the AREA tile rectangle and the anchor of
  AREA scenery (CE `location_xy` plus the object `+40,+20`). Shared diamond edges go to
  the larger map coordinate. The HumanFixed actor's retained foot projection
  `(40(y-x), 20(y+x)+20)` is 40 px left of that centre (checked on a zoom-2 capture on
  2026-10-16): a settled actor stands on the left vertex of its diamond. Picking
  follows the ground, not the sprite; correcting the actor offset is a separate change.
- **Driving.** `AdvanceWorldGrid` closes the same complete-observation frontier as
  before. Pending stick/neutral receipts run first as one ordinary advance and the
  follower only observes it; with none pending, `RouteFollower.Drive` runs the
  sub-advances to the frontier through the same `DiagnosticGridActor.Advance` and
  presentation selection, and every sub-advance's evidence and events are logged.
  A drive failure is the ordinary observation fault (`GRID_ROUTE_*`).
- **Stops.** An active stick direction stops the route at once
  (`ROUTE_STOPPED_BY_STICK`) and keeps its own intent. Every quarantine stops it
  (`ROUTE_STOPPED_QUARANTINE`): the palette, focus loss, device changes, settings
  saves, Fit, Reset scene and Reload. A started step is never cancelled by a stop or
  a new click: it ends at its cell centre, then the follower clears its own intent.
- **HUD.** `LMB click-to-move route <status> target=<cell> <reason> plan=<reason>
  segments=<n>`; the log prints `ARC-WORLD-007 route click` and `route stop` lines.

## Evidence

- Synthetic unit tests: `tests/Arcanum.Simulation.Tests/Navigation` (straight-line
  rule, segment cap versus goal, detour with diagonal shoulders, refusals, enclosed
  and Unknown goals, window and expansion bounds, one-click far walk in three
  segments, retarget mid-step without cancel, denial, stop, step-boundary
  sub-advances) and `tests/Arcanum.TerrainViewer.Tests/AreaRouteSourceTests.cs`
  (detour around the fixture blocker through `AreaGridSession`, verdict mapping).
- Original data, opt-in: `AreaRouteOriginalTests` in the Terrain Viewer tests with
  `ARC_AREA_ORIGINAL=1` and the
  [area coordinator roles](../rendering/crash-site-area-viewer.md). On the development
  Mac (2026-10-16): spawn (6,1) to (4,1) detours around the scenery at (5,1);
  occupied goals (5,1) and (3,1) end `ROUTE_NOT_FOUND` from their eight entries
  without a search; of eight goals 20 cells from spawn four were found and walked to
  arrival through the session (plans 0.1–3.4 ms); the other four are cells no step
  can enter at T0 and end `ROUTE_NOT_FOUND` from their eight entries.
- Picking: `tests/Arcanum.TerrainViewer.Tests/AreaCellPickingTests.cs` (diamond
  centre against `TerrainGeometry`, inside/outside each edge, the actor foot anchor
  on the left vertex, non-finite and far points).
- Native synthetic AREA probe (`ARC_AREA_GRID_PROBE=1`, see the
  [area viewer](../rendering/crash-site-area-viewer.md)): `ARC-WORLD-007 AREA route
  synthetic PASS` over the strict owner fixture: a detour around the blocker at
  (5,1) from an off-centre click, a new click mid-step (the started step completes),
  the same goal kept, a stick takeover in the route's own direction, the palette stop
  (step ends at its centre, a click under the palette is ignored), an occupied goal
  (`ROUTE_NOT_FOUND`, no step), and native dispatch (a ground click through
  `SubViewport.PushInput` sets the goal, a HUD click does not).
- Original data (development Mac, 2026-10-16, local check, not a sealed gate): in the
  running `crash-site-area` viewer a click inside the (3,3) diamond planned
  `ROUTE_LOCAL_SEARCH` and the actor arrived at (3,3) in one segment.
- Export: literal task `ARC-WORLD-007` in the closed terrain endpoint with exactly the
  ARC-RENDER-007 gate set; the route matrix runs inside its source and exported
  `area-grid` probes. `./arcanum area` prefers the current ARC-WORLD-007 build.

## NOT READY

No hover cursor or route preview, no physical mouse check (synthetic and local
software checks only), and the actor's 40 px foot offset is not corrected. No running,
NPC/object goals, doors, windows, traps, followers, other actors, state after T0,
movement speed or original path choice. Unknown or unsupported evidence is
reported, not explored. A target more than 32 cells away on an axis is refused,
not approached.
