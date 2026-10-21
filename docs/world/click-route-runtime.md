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

Project policy shaped by the bounded reading in the
[pathfinding study](../input/arcanum-pathfinding-study.md) of the original's path
function (0x41F3C0: the straight attempt and the 64×64 local search) and its rotation
helper (0x4B8D50), evidence level F0 (not checked in the executable). The structure and
limits below follow that reading; the code is independently written. Tie-breaking, the
open set, the turn cost model and repeated segmenting are project choices. The original's
special path flags (doors,
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
   Every step must be `Open` with no extra cost; the first other step ends the attempt
   with no partial route (`ROUTE_DIRECT` on success).
3. **Local search:** an eight-direction best-first search in a window of
   `2 × AxisDistance` (64) cells per axis, centred on the source/target box. Step cost
   10, +1 when the direction changes, plus the step source's extra cost
   (`IRouteStepSource.ExtraCost`, default 0; a window jump costs 50 as in the source,
   ARC-WORLD-021), estimate 10 × Chebyshev distance, at most
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

- `SetTarget(target, at, range)`: the goal is reached when the actor settles within `range` Chebyshev cells of it
  (0, the default, is the goal cell; a companion follow goal uses its follow range, ARC-WORLD-016,
  [companion follow](companion-follow-runtime.md)). The same goal with the same range is kept.
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

## Corridors beyond the local reach (ARC-WORLD-042)

`RoutePlanner` stays bounded (32 cells per axis, a 64-cell window, 4,096 expansions). For a follower built with
`RouteCorridorLimits` (the player's click route in the viewer opts in; a companion follow does not, so its follow goals
are refused beyond the local reach as before), a goal the source holds that the local planner refuses for its reach
(`ROUTE_TARGET_OUT_OF_RANGE`), its window (`ROUTE_WINDOW_EXHAUSTED`) or its expansions (`ROUTE_EXPANSION_LIMIT`) is
followed along a `RouteCorridor` instead:

- `RouteCorridor.Plan(source, from, target, limits)` searches every cell the source holds, eight directions, with the
  local planner's costs (10 a step, the source's extra cost, 1 a turn) and a Chebyshev estimate, within
  `RouteCorridorLimits` (65,536 expansions by default, at most 262,144; a 3×3-sector window has 36,864 cells). Its
  cells are cut into waypoints at most 24 steps apart (`WaypointSteps`, at most 24), each one within the local
  planner's reach of the one before, the last one the corridor's end.
- Outcomes: `Found` (`ROUTE_CORRIDOR`, the end is the target); `Partial` when no route reaches the target within the
  search: the end is the reached cell nearest it (Chebyshev, then cost), with `ROUTE_CORRIDOR_PARTIAL` (target held,
  not reached), `ROUTE_TARGET_OUTSIDE_AREA` (the source does not hold it) or `ROUTE_CORRIDOR_EXPANSION_LIMIT`;
  `NoRoute` when no cell was reached; `Refused` (`ROUTE_SOURCE_OUTSIDE_AREA`).
- The follower builds the corridor once per goal and walks it from waypoint to waypoint with the local planner (the
  same segments, step boundaries and denials as before). A leg that fails on the way (an occupant arrived on its
  waypoint) builds the corridor once more from the actor's cell; a second failure stops with the leg's reason. At the
  end of a partial corridor it stops with `ROUTE_PARTIAL`; a corridor that reaches no cell stops with its own reason. A goal outside the source is still refused (`ROUTE_TARGET_OUTSIDE_AREA`, no step): a host heads for
  one through its own partial corridor, as the streamed main map does ([map mode](../rendering/area-map-mode.md#the-main-world-map-arc-render-024)).
- Tests: `tests/Arcanum.Simulation.Tests/Navigation/RouteCorridorTests.cs` (a maze longer than the local reach cut into
  waypoints, partial and refused corridors and the limits, the follower walking the maze through its gaps, a walled-in
  goal beyond reach stopping `ROUTE_PARTIAL`, a goal outside the source refused, an occupant on a waypoint rebuilding the
  corridor once, a follower without corridor limits refusing as before).
- The corridor runs on the caller's thread when a leg is planned (the viewer's main thread). The native walk smoke
  reports its slowest frame, corridors included ([walk smoke](../rendering/area-map-mode.md#the-main-world-map-arc-render-024)).

## World routes across windows (ARC-WORLD-042)

A map too large to load whole (the main world map, streamed by window) has no single source to search. Its walking
route is `Arcanum.Data.Traversal.AreaWindowRoute.Plan(stack, catalog, mapName, from, to, limits, token)`: an
eight-direction search of one cell per step (Chebyshev estimate) over the
[window step evidence](area-step-assessment.md#windows-arc-data-034), each step assessed by a window whose core holds
both of its cells, loaded on demand around the step's sector and kept in a bounded cache. A step is taken only when
the window assesses it `Open`; a sector with no window (off the map, or a window that does not load complete) blocks
the route in that sector only. `AreaWindowRouteLimits`: window loads (256 by default, at most 1,024; a reload after
eviction counts again), expansions (1,000,000 by default, at most 8,000,000) and resident windows (24, at most 64, least
recently used dropped first). The window cache is bounded; the search's own state (costs, parents, the closed set and
the queue) grows with its expansions, so the expansion limit bounds it. Before the search a goal no window admits, or
that no step enters, is refused. Statuses: `Found` (`ROUTE_WINDOWS`, cells from start to goal), `NoRoute`
(`ROUTE_NOT_FOUND`), `LimitExceeded` (`ROUTE_WINDOW_LIMIT`, `ROUTE_EXPANSION_LIMIT`) and `Refused`
(`ROUTE_START_NOT_ADMITTED`, `ROUTE_GOAL_NOT_ADMITTED`, `ROUTE_GOAL_UNENTERABLE`, `ROUTE_WINDOW_EVIDENCE_<code>` when a
window's evidence cannot be built). Cancellation throws `OperationCanceledException`; a read error of the content stack
escapes as its own exception. It is a planning aid for long walks (the native walk smoke steers by it), not the
original's path search.

- A goal carried across a window move of the streamed main map: `AreaMapGoalHandOff.Resume` (Terrain Viewer Core)
  keeps a goal the new window holds; a goal outside it is approached through the end of a partial corridor over the new
  window (the reached cell nearest it, so greedy: it can end at a dead end, such as the edge of the Crash Site plateau,
  whose way out goes south first) and handed on to the next move (`AreaMapGoalHandOffTests`).
- The native walk smoke walks the route above in the viewer with no seam
  ([walk smoke](../rendering/area-map-mode.md#the-main-world-map-arc-render-024)).

- On the installed original (development Mac, 2026-10-21, `AreaWindowStepRealDataTests`): the new-game start
  (92958, 82592) to the middle of the Shrouded Hills town sector (90208, 84128) is found: 3,106 cells (the straight
  line is 2,750) through 88 sectors, off the plateau south first, with 51 window loads (49 before the goal check ahead of the search was added) and 143,401 expansions in 157 s.
- Synthetic: `AreaWindowStepTests` (the 3×3-sector fixture map: a route across sectors through windows loaded on
  demand, one resident window, the window-load and expansion limits, a start off the map, a goal off it refused before
  any search).

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
  AREA scenery (the original's location-to-screen function (0x4B8680) plus the object `+40,+20`). Shared diamond edges go to
  the larger map coordinate. Since ARC-RENDER-011 a settled actor stands on that
  centre too ([actor anchor](../rendering/terrain-viewer.md#actor-anchor-and-wall-fade-arc-render-011));
  before it the actor was drawn 40 px left, on the left vertex of its diamond. Picking
  follows the ground, not the sprite.
- **Driving.** `AdvanceWorldGrid` closes the same complete-observation frontier as
  before. Pending stick/neutral receipts run first as one ordinary advance and the
  follower only observes it. A following route also waits while a stick receipt is
  still open after the frontier (a release observed just after a click), so it never
  starts a step at an earlier step end that the release would cancel. Otherwise
  `RouteFollower.Drive` runs the
  sub-advances to the frontier through the same `DiagnosticGridActor.Advance` and
  presentation selection, and every sub-advance's evidence and events are logged.
  A drive failure is the ordinary observation fault (`GRID_ROUTE_*`).
- **Stops.** An active stick direction stops the route at once
  (`ROUTE_STOPPED_BY_STICK`) and keeps its own intent. Every quarantine stops it
  (`ROUTE_STOPPED_QUARANTINE`): the palette, focus loss, device changes, settings
  saves, Fit, Reset scene and Reload. A started step is never cancelled by a stop or
  a new click: it ends at its cell centre, then the follower clears its own intent.
- **HUD.** `LMB click-to-move route <status> target=<cell> <reason> plan=<reason>
  segments=<n> (last click <cell> goal set | same goal kept | ignored ...)`; the log prints `ARC-WORLD-007 route click` and `route stop` lines.

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
  the same goal kept, a stick takeover in the route's own direction, a click observed
  just before a stick release (no route step is cancelled), the palette stop
  (step ends at its centre, a click under the palette is ignored), an occupied goal
  (`ROUTE_NOT_FOUND`, no step), and native dispatch (a ground click through
  `SubViewport.PushInput` sets the goal, a HUD click does not).
- Original data (development Mac, 2026-10-16, local check, not a sealed gate): in the
  running `crash-site-area` viewer a click inside the (3,3) diamond planned
  `ROUTE_LOCAL_SEARCH` and the actor arrived at (3,3) in one segment.
- Export: literal task `ARC-WORLD-007` in the closed terrain endpoint with exactly the
  ARC-RENDER-007 gate set; the route matrix runs inside its source and exported
  `area-grid` probes. `./arcanum area` prefers the current ARC-WORLD-007 build over
  older AREA builds (an ARC-WORLD-009 cave build, which includes the route, comes first).

## NOT READY

No hover cursor or route preview. Physical mouse: only the owner's one-line ARC-INPUT-002 check
(2026-10-17: LMB on the ground sets the goal; [result](../input/diagnostic-palette.md#result-2026-10-17));
everything else is synthetic and local software checks. No running,
NPC/object goals, doors, windows, traps or state after T0 other than the live PC and companions of
[ARC-WORLD-016](companion-follow-runtime.md#live-actors-over-t0-evidence-terrain-viewer-core),
movement speed or original path choice. Unknown or unsupported evidence is
reported, not explored. A goal beyond the local planner's 32-cell reach is followed along a corridor over the
source (ARC-WORLD-042). On the main map a click is a goal inside the shown window; a goal outside it arises only when a
walk bends out of the window, and is approached greedily (above); a farther goal is walked only from a host's plan (the
walk smoke's window route). The corridor and the window route are this project's searches (shortest by their costs),
not the original's path choice; their main-thread cost is measured only by the walk smoke's slowest frame.
