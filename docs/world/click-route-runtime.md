# Click-to-move route (ARC-WORLD-007)

Status: **Implemented**, stage 1 (Godot-free core) and stage 2 (LMB in the
`crash-site-area` viewer). The core plans a bounded route to a clicked cell over the
caller's step evidence and turns it into ordinary
[movement runtime](grid-movement-runtime.md) intents; the viewer picks the clicked
ground cell and drives the actor there ([viewer binding](#viewer-binding-stage-2)).
`world-diagnostic` has no click route. Since ARC-WORLD-073 the planner is the original's path request and the follower
walks as the original's walk ([original check](#original-check-arc-world-060)), read in the executable (F2); runtime parity
is **NOT_VERIFIED** (no original-runtime run).

Requirements: [FR-NAV-001](../requirements/world-travel-runtime.md#fr-nav-001)
(click is a route goal), [FR-NAV-002](../requirements/world-travel-runtime.md#fr-nav-002)
(the route uses the step evidence the actual step uses),
[FR-NAV-004](../requirements/world-travel-runtime.md#fr-nav-004) (segment cap is not
a goal limit) and KM-03 in [original keyboard and mouse](../requirements/original-keyboard-mouse.md).

## Provenance

ARC-WORLD-073: the original's path request (0x41F3C0, the gate, the direct attempt 0x41F840 and the 64 × 64 search
0x41F9F0) and the walk around it (0x426040, the budget 0x426320, the segments and the PC's straight-line retry), read in
the retail executable ([ledger](../research/original-verification-ledger.md#long-and-far-routes-arc-world-060) LR1–LR9 and
DC2–DC8, two readers, F2). One search, `OriginalPathRequest` (`Arcanum.Simulation.Navigation`), serves the click route, its
corridor's core and the drop's placement (ARC-COMBAT-010, `AreaDropMap`); a test checks it returns what the drop's own port
returned on random invented maps. The code is independently written. The request's flags 0x200 (light costs), 0x400 (fire
costs) and 0x1000 (another routine, unused) are not modelled; the step source decides every step (doors and windows by
their runtime passage, traps and fire not yet).

## Planner: `RoutePlanner.Plan`

`Arcanum.Simulation.Navigation` (no Data, Input or Godot dependency):

```csharp
RoutePlan RoutePlanner.Plan(IRouteStepSource source, CellCoord from, CellCoord target, RouteLimits? limits = null);
RoutePlan RoutePlanner.StraightRetry(IRouteStepSource source, CellCoord from, CellCoord target, RouteLimits? limits = null);
RoutePlan RoutePlanner.PlanApproach(IRouteStepSource source, CellCoord from, CellCoord target, RouteLimits? limits = null);
interface IRouteStepSource { bool Contains(CellCoord cell); RouteStepVerdict Assess(CellCoord from, NeighborDirection direction);
    int ExtraCost(...) => 0; bool StepRefusedByObject(...) => false; bool GoalHoldsCritter(CellCoord goal) => false;
    bool StepEntersCritter(CellCoord from, NeighborDirection direction) => false; }
```

1. **Refusals, never searched:** source or target outside `Contains`
   (`ROUTE_SOURCE_OUTSIDE_AREA`, `ROUTE_TARGET_OUTSIDE_AREA`); more than
   `AxisDistance` (32) cells on either axis (`ROUTE_TARGET_OUT_OF_RANGE`: the direct attempt refuses more than 32 on the
   larger axis and the search 32 or more on either). The same cell is `AlreadyAtTarget`.
2. **The goal's gate** (DC2): some admitted neighbour enters the goal by an `Open` step and no living critter stands on it
   (`GoalHoldsCritter`, while the plan passes critters) and it is not a cell the follower avoids, else `ROUTE_NOT_FOUND`
   (`ROUTE_EVIDENCE_INCOMPLETE` if one of those steps is Unknown), answered from the eight entries alone.
3. **Direct attempt** (DC3): from each cell step toward the target, diagonally unless the minor remaining delta is under
   half of the major one (`RoutePlanner.Toward`, 0x4B8D50). Every step must be `Open` and cross no window piece (no extra
   cost); the first other step ends the attempt with no partial route (`ROUTE_DIRECT` on success).
4. **Search** (DC4) when both axes are under 32 apart (else `ROUTE_SEARCH_OUT_OF_RANGE`): over a 64 × 64 window centred on
   both cells, each round takes the open cell of the smallest cost + 10 × Chebyshev distance, the first in row-major
   order on a tie (start cost 1); a step costs 10, 50 more across a window piece (a positive `ExtraCost`), 1 more when it
   turns; a cheaper cost opens or reopens a cell; a cell whose estimate over 10 exceeds the budget
   (`RouteLimits.Budget`, at most 200) is dropped; a refused cardinal step the source says an object refused
   (`StepRefusedByObject`, not a wall or portal) drops its cell (`ROUTE_LOCAL_SEARCH` on success). Cells outside the window
   are never asked about. The route is the original's choice by its costs, not a shortest one.
5. **No route:** `ROUTE_BUDGET_EXHAUSTED` when the budget dropped a cell, otherwise `ROUTE_EVIDENCE_INCOMPLETE` when an
   `Unknown` step was met, otherwise `ROUTE_WINDOW_EXHAUSTED` when the window edge cut the search, otherwise
   `ROUTE_NOT_FOUND`. `ROUTE_COORDINATE_OVERFLOW` is a defensive refusal only.
6. **`StraightRetry`** (LR4, 0x426040 with flags 0x78): a straight line toward the target, every step accepted and none
   tested when planned (`ROUTE_STRAIGHT_RETRY`, method `StraightRetry`); nothing beyond 32 cells.
7. **`PlanApproach`**: the attack's approach ([below](#attack-approach-arc-world-073)).

Only `Open` is traversable; `Blocked`, `Unknown` and `Unsupported` are never promoted.
Each directed step is asked at most once per plan; the plan reports the counts of
assessments, expansions and Blocked/Unknown/Unsupported steps. `RoutePlan.Steps` is
the whole route, `Cells` the cells it enters and `Segment` its first
`SegmentSteps` (8) steps, the original's cap. `RouteLimits` values can be lowered, never raised; `RouteLimits.WalkBudget`
is the walk's budget, `min(4 × distance + 5, 200)` (LR2, 0x426320).

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
  `Drive` call), unless the host asked for `replanOnDenial` (the viewer's click route, ARC-WORLD-073, below): then
  the next plan avoids the refused cell. A new click after a stop starts cleanly.
- While a goal is followed it owns movement and stops the actor on the goal even if
  another source set the same direction; otherwise the follower clears only an intent
  it issued itself, so a host may keep driving it while another input source moves the
  actor. Ownership is the receipt key of the follower's last input, not the intent
  value: after a stop, a stick that sets the same direction owns it. With no goal and
  no intent of its own the follower returns the host's target directly, so it never
  splits another source's advance at a step end. Observation times gate a settled actor only; a goal change during a step
  applies at that step's end. `Next` plans as it decides, so `LastPlan`,
  `PlannedSegments` and a plan failure are recorded even if the host drops that advance.
- ARC-WORLD-073, the original's walk: every plan asks `RouteLimits.WalkBudget` from the cell the walker will stand on (a
  pop between two plans clears the original's budget, so every plan is a first one, LR2 as corrected). A follower built
  with `straightRetry` (the viewer's click route; a companion follow has none) takes `StraightRetry` when a plan finds no
  path, and from then on every plan toward that goal is the straight line again (the run's flag 0x400 survives); the
  movement tests each step and a refused one stops the walk (`ROUTE_STEP_DENIED`), as the original ends the run.
- ARC-WORLD-074: the PC's own steps pass living critters outside combat mode (`AreaGridSession.CrittersPass`, set by the viewer
  as "not in combat mode", read once per advance): T0 PC and NPC records count as moved and the live actors (companions) hold no
  cell for the PC, through the same `AreaOccupancyRules.ApplyPassingCritters` the click route plans with; objects, walls and doors
  keep blocking. Tests: `tests/Arcanum.TerrainViewer.Tests/AreaCritterPassTests.cs` (through an NPC and a companion outside combat,
  blocked in combat, the goal holding an NPC refused, the Original segment-end stop and Steamwoven walking through).
- ARC-WORLD-073, living critters (owner decision 2026-10-21 12:09, "outside combat NPCs do not block the way"): the viewer's click
  route plans through them outside combat mode in both rule modes, refuses a goal that holds one, and with `segmentEndCritterEndsWalk`
  (Original, LR5) ends the walk before a segment's last step onto one (`ROUTE_SEGMENT_END_CRITTER`). With `replanOnDenial` a step the
  movement refuses during a normal plan (since ARC-WORLD-074 not a critter's tile outside combat mode: a door that closed, an
  object that changed) makes the next plan avoid that cell, at most
  `DenialReplanMaximum` (8) cells a goal, after which a refused step stops with `ROUTE_STEP_DENIED`; so no walk stalls: it goes
  round, or stops with a typed reason. A diagonal step refused for a critter on its shoulder avoids the step's destination, not
  the shoulder (bounded by the same limit). Critters the session killed are dead for these checks. A leg of Steamwoven's
  corridor asks the whole budget (200), since the corridor exists to pass the walk's budget.
- `Drive` runs `Next`/`Observe` through the host's advance up to the frame target,
  at most 1,024 sub-advances per call, and returns every `MovementResult` in order for
  presentation.

## Corridors beyond the local reach (ARC-WORLD-042)

`RoutePlanner` stays the original's bounded request (32 cells per axis, a 64-cell window, the walk's budget). For a
follower built with `RouteCorridorLimits` (the player's click route in the viewer opts in; a companion follow does not, so
its follow goals are refused beyond the local reach as before), a goal the source holds that the request refuses for its
reach (`ROUTE_TARGET_OUT_OF_RANGE`, `ROUTE_SEARCH_OUT_OF_RANGE`), its window (`ROUTE_WINDOW_EXHAUSTED`) or its budget
(`ROUTE_BUDGET_EXHAUSTED`) is followed along a `RouteCorridor` instead, under Steamwoven only (below); the original has no
long route (LR7):

- `RouteCorridor.Plan(source, from, target, limits)` runs the shared search's core (ARC-WORLD-073) over every cell the
  source holds, with the request's costs (10 a step, 50 more across a window piece, 1 a turn) and estimate, no budget, within
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
- The far click is a rule-mode switch (ARC-WORLD-072, ledger LR4/LR7, owner decision 2026-10-21): a follower may take
  `corridorAllowed`, asked once when a goal is set. The viewer's click route answers from the session's
  `Rules.Navigation.FarClickDoesNotMove`: under Original no corridor is built; a goal beyond the 32-tile reach stops with
  `ROUTE_TARGET_OUT_OF_RANGE` and the PC does not move, as in the original. Since ARC-WORLD-073 a near goal the request
  cannot reach (window, budget) also gets no corridor under Original: the PC's straight-line retry (LR4) walks toward it
  until a step is refused; under Steamwoven the corridor comes first and the retry after a corridor that reaches nothing.
  A corridor walk already started goes on after a switch;
  the next goal set reads the new value, the same goal clicked again included (a changed answer makes it a new goal), and
  a goal carried across a main-map window move is set again and so reads it too. The hover's ground action follows it
  (*no route* beyond 32 tiles under Original).
- Tests: `tests/Arcanum.Simulation.Tests/Navigation/RouteCorridorTests.cs` (a maze longer than the local reach cut into
  waypoints, partial and refused corridors and the limits, the follower walking the maze through its gaps, a walled-in
  goal beyond reach stopping `ROUTE_PARTIAL`, a goal outside the source refused, an occupant on a waypoint rebuilding the
  corridor once, a follower without corridor limits refusing as before, the far-click switch in both modes and a switch
  change applied at the next goal, a near goal with a corridor under Steamwoven and the straight-line retry under Original,
  the same goal clicked again after a switch). The map-mode native smoke's map stage (`ARC-WORLD-072 native-smoke far click`) takes the
  first cell 40 tiles from the start reachable over the shown chart (over no exit tile) and clicks it under Original (refused with
  `ROUTE_TARGET_OUT_OF_RANGE`, the PC does not move) and under Steamwoven (walked along a corridor to the goal). It then puts
  the PC back and restores the session's mode. A map without such a cell reports NONE (the Crash Site cave). On the main map
  at the Crash Site, 2026-10-21: 40 tiles, a corridor of 2 waypoints, 11 smoke frames.
- The corridor runs on the caller's thread when a leg is planned (the viewer's main thread). The native walk smoke
  reports its slowest frame, corridors included ([walk smoke](../rendering/area-map-mode.md#the-main-world-map-arc-render-024)).

## Attack approach (ARC-WORLD-073)

The original's approach for an attack beyond reach, read in the executable by two readers
([ledger](../research/original-verification-ledger.md#attack-approach-arc-world-073) AP1–AP7):
`RoutePlanner.PlanApproach(source, from, target)` asks the shared request for a route to the target's own tile with the
request's flag 0x1 (the goal's gate skipped, the step onto the goal taken untested) and the walk's budget, and ends one step
short of it; the cell it ends on is `RoutePlan.Target` (`ROUTE_APPROACH`). No route, or a route of no step (the target next
to the walker), is `NoRoute` (`ROUTE_APPROACH_ADJACENT` for the latter): the attack ends, as the original's after about a
second outside a round. There is no straight-line retry, and living critters block the route in and out of combat mode (the
approach never sets the request's flag 0x10). The viewer's attack (`AttackApproachCell`) walks the click route to that cell,
which a route reaches by construction (the COMBAT-014 limit of a reach cell with no route is gone); the weapon's reach is
tested when the walk ends, and a ranged attacker walks to the same cell (AP7). The object-attack smoke picks its start where
this approach finds a route. Use, pick-up and talk approaches keep `ApproachCell` (the original's goal 8, AP6, is
ARC-WORLD-068's).

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

## Original check (ARC-WORLD-060)

The original's walk to a clicked tile is read in the retail executable
([ledger](../research/original-verification-ledger.md#long-and-far-routes-arc-world-060) LR1–LR8, with the path request
DC2–DC8 and the freed cells FC1–FC3). Each part of this runtime against it:

| Part | Original | Steamwoven | Status |
| --- | --- | --- | --- |
| Segments and replanning (`RouteFollower`) | at most 8 rotations a plan, then a new plan from the walker's tile toward the same goal (LR1, LR3); a new click replaces the goal (LR3) | 8-step segments, a new plan after each; a new goal at the next cell centre | Follows |
| Straight attempt and search reach (`RoutePlanner`) | straight up to 32 tiles (larger axis; skipped with flag 0x200, LR9), search under 32 on either axis in a 64 × 64 window; no partial route (LR1, DC3, DC4) | the shared request (ARC-WORLD-073) | Follows (flag 0x200 not modelled) |
| Search bound and costs | the budget `min(4 × distance + 5, 200)` on every plan (a pop clears it between plans, LR2 as corrected), drops long detours; 10 a step, 1 a turn, 50 a window, 90 a spotted trap, 80 fire with flag 0x400, light with flag 0x200 (LR2, LR9, DC4) | the shared request with the walk's budget; 10, 1 and 50 | Follows (traps, fire and light not yet in the step source) |
| Critters | ignored by the plan outside combat mode, checked only before a segment's last step; a hit ends the run, no re-route (LR2, LR5, LRA1); in combat mode they block | owner decision 2026-10-21 12:09: outside combat mode the plan passes living critters in both modes (`AreaRouteSource` `crittersPass`), a living critter on the goal still refuses it (DC2, `GoalHoldsCritter`); in combat mode they block; the segment-end stop is the switch `SegmentEndCritterEndsWalk` (Original stops with `ROUTE_SEGMENT_END_CRITTER`, Steamwoven walks on) | Follows: since ARC-WORLD-074 the PC's steps pass them too outside combat mode (`AreaGridSession.CrittersPass`, `AreaOccupancyRules.ApplyPassingCritters`); a companion's own walk does not yet (NOT READY below) |
| No route | a PC tries once more in a straight line with every step accepted, tested as it walks, and keeps planning straight lines for the rest of the walk (LR4 as extended) | `StraightRetry` for the click route's follower, kept for the goal | Follows |
| A goal beyond 32 tiles (the corridor, ARC-WORLD-042) | refused: the PC does not move (LR4, LR7); at the original's fixed view such a click does not occur | followed along a corridor over the shown window, rebuilt once around an occupant | Switch (owner decision 2026-10-21, ARC-WORLD-072): Steamwoven walks the corridor, Original does not move (`NavigationRuleProfile.FarClickDoesNotMove`); since ARC-WORLD-073 Original builds no corridor for a near goal either |
| Sector borders, the main map's windows | tiles locked on demand through the 16-sector cache, no border (LR6) | a window of 3 × 3 core sectors, moved as the actor walks; the far-goal hand-off carries a goal across a move | Host limit (no rule) |
| Long walks (`AreaWindowRoute`) | none: far travel is the world map's (LR7) | a planning aid for the walk smoke, not a player rule | Tooling (no rule) |
| Destroyed and switched-off objects | never listed, so they never block a step (FC2) | the PC's live overlay moves them (ARC-WORLD-065); the click route, its corridor and the far-goal hand-off read that overlay; since ARC-WORLD-060 `AreaWindowRoute` takes the host's step verdict (`stepOpen`) and the walk smoke passes the overlay's removed objects (not door state or live actors, which the walk's own route applies); the hover cursor's route source does not read the overlay yet (ARC-WORLD-068's) | Follows (the hover: open) |
| The attack's approach | a route to the target's tile with flag 0x1, one step short; critters block; no retry (AP1–AP7) | `RoutePlanner.PlanApproach`, the viewer's attack walks to its cell | Follows for the cell; the walk to it uses the click route's rules (NOT READY below) |

Since ARC-WORLD-073 one search serves the game: `OriginalPathRequest`, which `RoutePlanner` (the click route), the core of
`RouteCorridor` (Steamwoven's far-click aid, no original counterpart) and the drop's port `AreaDropMap` (ARC-COMBAT-010,
results unchanged) all use. `AreaWindowRoute` stays a separate planning aid for the walk smoke.

## NOT READY

No hover cursor or route preview. Physical mouse: only the owner's one-line ARC-INPUT-002 check
(2026-10-17: LMB on the ground sets the goal; [result](../input/diagnostic-palette.md#result-2026-10-17));
everything else is synthetic and local software checks. No running,
NPC/object goals, doors, windows, traps or state after T0 other than the live PC and companions of
[ARC-WORLD-016](companion-follow-runtime.md#live-actors-over-t0-evidence-terrain-viewer-core),
movement speed or original path choice. Unknown or unsupported evidence is
reported, not explored. A goal beyond the local planner's 32-cell reach is followed along a corridor over the
source (ARC-WORLD-042; Steamwoven only since ARC-WORLD-072/073). Since ARC-WORLD-074 the PC steps through living NPCs and
companions outside combat mode in both rule modes, and may stand on a cell one holds (a walk stopped there by a new click); in
combat mode they block. A companion's own walk does not pass critters yet: the original gives an NPC mover the same flag 0x10
outside combat (LR2), which is the companions lane's follow-up (NOT READY; its admission and planning overlays,
`AreaCompanionGroup.OccupancyFor`, are unchanged), so a companion still stops for the PC and the other companions. The attack's approach walks to its cell with the
click route's rules (its plan passes critters outside combat mode, and it has the straight-line retry), while the original's
approach keeps critters blocking and has no retry; a step refused on
the way is planned around as for any click walk (the original re-plans in goal 79, AP5, F1); a target that moves while the PC walks is
re-approached only when the walk ends. Traps, fire and light costs are not in the step source yet. On the main map a click is a goal inside the shown window; a goal outside it arises only when a
walk bends out of the window, and is approached greedily (above); a farther goal is walked only from a host's plan (the
walk smoke's window route). The corridor and the window route are this project's searches (shortest by their costs),
not the original's path choice; their main-thread cost is measured only by the walk smoke's slowest frame.
