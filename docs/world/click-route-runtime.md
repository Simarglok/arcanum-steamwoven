# Click-to-move route core (ARC-WORLD-007)

Status: **Implemented Godot-free core**, stage 1. It plans a bounded route to a
clicked cell over the caller's step evidence and turns it into ordinary
[movement runtime](grid-movement-runtime.md) intents. Stage 2 binds LMB in the
`crash-site-area` viewer to it; until then no mouse click moves the actor
(`LMB click-to-move PENDING`, [palette route](../input/diagnostic-palette.md)).
Original pathfinder and runtime parity are **NOT_VERIFIED**.

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
   keeps one entry per cell, so the turn cost only shapes which of the shortest routes
   is chosen; it is not an exact turn-minimal search, and nothing here claims original
   path quality.
4. **No route:** `ROUTE_EVIDENCE_INCOMPLETE` when an `Unknown` step was met (a route
   may exist through it), otherwise `ROUTE_WINDOW_EXHAUSTED` when the window edge cut
   the search (a route may exist outside it), otherwise `ROUTE_NOT_FOUND` (only
   Blocked/Unsupported steps closed it); `ROUTE_EXPANSION_LIMIT` when a lowered budget
   ran out. `ROUTE_COORDINATE_OVERFLOW` refuses cells at the edge of the `int` range.

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
- The follower clears only an intent it issued itself, so a host may drive it while
  another input source owns movement. `Next` plans as it decides, so `LastPlan`,
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
  occupied goals (5,1) and (3,1) end with no route after about 2,000 expansions
  in 25–50 ms (`ROUTE_WINDOW_EXHAUSTED`: the search reached the window edge); of eight goals 20 cells from spawn four were found and walked to
  arrival through the session (plans 0.1–1.2 ms), four had no route inside the window.

## NOT READY

LMB/hover binding, screen-to-cell picking and the cursor are stage 2. No running,
NPC/object goals, doors, windows, traps, followers, other actors, state after T0,
movement speed or original path choice. Unknown or unsupported evidence is
reported, not explored. A target more than 32 cells away on an axis is refused,
not approached.
