# Companion follow runtime (Virgil follows the PC)

Status: **Implemented bounded component**, ARC-WORLD-016 (slice 3 of the
[companion research](../research/companion-follow-evidence.md#proposed-implementation-slices-project-policy-not-accepted),
ARC-RES-024). After Virgil joins the [session party](party-runtime.md) (ARC-WORLD-013) he walks after the PC in the
`crash-site-area` viewer: Godot-free follow rules in `Arcanum.Simulation.Party`, a second moving actor and the live
actor overlay in Terrain Viewer Core, and the viewer wiring (his dialog now runs through the session party port). It
covers the follow part of [FR-SOCIAL-003](../requirements/story-social-ai.md#fr-social-003) and the waiting part of
[FR-SOCIAL-005](../requirements/story-social-ai.md#fr-social-005) in the viewer.

**NOT READY:** no walk cycle or turning for Virgil (his T0 body frame moves with him), no running speed (the source's
run-near goal moves at the diagnostic walking speed), no carrying across a map change (in the cave he is not there;
he waits on the exterior where he stood, ARC-WORLD-017), no orders, loyalty, wait timer, combat or turn-based cost, no
refusal line text, and the PC side of the follow check is still the AREA diagnostic profile (charisma 8, level 1), not
the session hero. Distances and order are source claims read in CE at `a7ff41b300ef712f0e7d088183a3d08957110cdd`;
original executable parity is **NOT_VERIFIED**.

## Follow rules (`Arcanum.Simulation.Party`)

`FollowDistanceRules.Decide(follower, leader, spreadOut, approaching)` is a pure decision over Chebyshev cell
distances ([C7–C10](../research/companion-follow-evidence.md#findings)):

| Distance d to the leader | No follow goal | Follow goal alive |
| --- | --- | --- |
| d > 30 | `Jump` | `Jump` |
| d = 0 (shared cell) | `Separate` | `Separate` |
| d ≤ 4 (7 spread out) / ≤ 2 (5) | `Hold` | `Hold` when d ≤ 2 (5) |
| otherwise | `Walk` within range + 2, else `Run` | same |

`CompanionFollower` turns a decision into a [`RouteFollower`](click-route-runtime.md#follower-routefollower) goal on
the leader's committed cell with the follow range (`SetTarget(target, at, range)`, new in this task: the goal is reached
when the actor settles within `range` of it; a click route uses 0). So the follower walks through the same bounded
planner, exact movement and step evidence as a click route, re-plans at each step end toward the leader's current
cell, and never cancels a started step. The host calls `Update(state, leader, spreadOut, following, frontier)` once
per frame and then `Drive` to the same frontier:

- **Following** (session standing `Following`): beyond the start distance a goal starts; while it is alive it follows
  the leader's new cell and stops within range. Decisions use the leader cell of that frame, not the source's 100 ms
  re-check.
- **Not following** (waiting or free): the goal stops (`FOLLOW_NOT_FOLLOWING`); a started step ends at its cell centre.
- **Unreachable:** a goal that ends without arriving (`ROUTE_NOT_FOUND`, a denied step) is not planned again until
  the follower or the leader stands on another cell, so a closed-off leader costs one plan, not one per frame.
- **Jump:** beyond 30 cells the update returns the leader's cell; the host stands the follower settled there. On
  the shared cell the next decision steps it to the first Open neighbour in direction order (C10).

## Live actors over T0 evidence (Terrain Viewer Core)

The AREA step evidence is the new-game T0 snapshot, in which living critters block their serialized cell. With a
moving companion that is no longer true, so `AreaOccupancyRules.Apply(assessment, from, direction, occupancy)` overlays
live actors on every directed step (project policy):

- A **moved** T0 record (each companion's) no longer blocks, and its evidence gaps no longer count.
- A **live actor** blocks like a living critter in the T0 object rules: on the destination
  (`DESTINATION_ACTOR_OCCUPIED`) and, for a diagonal step, on either shoulder (`SHOULDER_ACTOR_OCCUPIED`). An actor
  holds its committed cell and the other cell of a started step.
- The Data precedence is kept: terrain, wall, then object blockers (remaining T0 records, then live actors), then
  gaps. A step refused for terrain, a wall, the core edge or an unavailable path branch is never changed.

`AreaGridSession.Occupancy` (read at each advance) and `AreaRouteSource(…, occupancy)` apply the same overlay, so a
planned step is the step the runtime admits (FR-NAV-002). The evidence keeps the T0 assessment and adds `Live`, the
verdict the step was admitted with, when the overlay changed it. The PC is admitted against the companions; each
companion against the PC and the other companions, except that a companion **plans** toward the PC without the PC's
cells (the goal is the PC's cell; with range ≥ 2 the route never enters it).

`AreaCompanionGroup` holds the measured companions of one map (`AreaCompanionRoster`: Virgil only, matched by prototype
17102 and script 1324, with his ARC-RES-026 inputs). Each companion has its own `AreaGridSession` (actor id
`AREA_COMPANION_<speaker>`) and `CompanionFollower`; `Drive(session, pc, frontier)` runs them after the PC's advance.
`Commands(plan)` draws the NPC's T0 body command moved by the projected offset of his continuous anchor and ordered
at his committed cell; `AreaUprightOrder.With(…, moving)` takes the T0 body out and inserts the moved one where a
stable sort would.

## Session state

The party standing is the session's ([party runtime](party-runtime.md)). Where a companion stands is one map-local
entity change of the NPC (`MapChangeKey.ForEntity("oid:<sha>", "npc.cell")` on his map, project-defined), the world
cell and facing packed into one value (`x << 32 | y << 3 | facing`, cells below 2^28), so a placement is one atomic
change; absent means his T0 cell. No snapshot schema change: schema 6 already carries map changes. Like the PC's
placement it is written only at a boundary: F7 (a refused write refuses the save) and leaving the map. A group is
rebuilt from the session whenever the PC's grid session is (spawn, Reset scene, Reload, a transfer, F8); a placement
the chart cannot stand is skipped and reported, not guessed.

## Viewer binding

- `AreaConversationHost(…, party: true)`: a roster speaker talks with `SessionDialogPartyPort` and his measured facts
  (AI row 5 −1000/2001/2001/30, level 1, alignment 1; the diagnostic PC). His `JO 0` joins the session party, `WA`
  and `UW 0 518` run, and node 385 (his follower entry) is no longer a frontier. Other speakers keep the diagnostic
  `follow.pc` key. A refused JO/UW shows `follow refused: <reason>` after the dialog closes.
- Talk reach, walk-to-talk and the HUD prompt use where the companion stands now.
- HUD: `companion script 1324 <standing> at (x,y) <phase> follow=<action> route=<status> jumps=<n>`; the log prints
  `ARC-WORLD-016 companion …` step lines.

## Tests

- `tests/Arcanum.Simulation.Tests/Party/CompanionFollowTests.cs`: the distance table, approach to range 2 through the
  route, a moving leader, the unreachable back-off, the jump and the shared-cell step, waiting mid-step, spread out
  and the route range.
- `tests/Arcanum.TerrainViewer.Tests/AreaCompanionTests.cs`: the overlay rule (moved record, destination and shoulder
  actors, precedence, core edge), placement packing, a companion following the PC through the session party and drawn
  on its cell, waiting, the jump, and the placement surviving a save and load.
- `tests/Arcanum.TerrainViewer.Tests/VirgilFollowOriginalTests.cs` (opt-in `RealData`, `ARC_AREA_ORIGINAL=1` and the
  `ARC_AREA_*` roles): on the authenticated exterior the viewer host joins Virgil through his first talk, node 385
  then opens, and from his T0 chart cell (31,34) he follows a PC at (42,33) to (40,34) in two plans; the cell survives
  a save and load. Development Mac, 2026-10-16.
- Native smoke (`--native-smoke --mode crash-site-area` with the original roles): `ARC-WORLD-016 native-smoke follow
  PASS` after the quick-save stage: Virgil joins through his dialog in the running viewer, follows the PC to within 2,
  is drawn on his new cell only, his cell is in the session, and a waiting Virgil stays while the PC is farther than 4.
  `ARC_AREA_FOLLOW_CAPTURE=<png>` saves the frame.

```sh
"$DOTNET" test tests/Arcanum.Simulation.Tests --no-build --filter "FullyQualifiedName~CompanionFollow"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests --no-build --filter "FullyQualifiedName~AreaCompanion"
ARC_AREA_ORIGINAL=1 ARC_AREA_MODULE=… ARC_AREA_RESOURCE1=… ARC_AREA_RESOURCE2=… ARC_AREA_MOBILE=… ARC_AREA_PROTOTYPES=… \
  ARC_AREA_EFFECTS=… ARC_AREA_PATCH=… "$DOTNET" test tests/Arcanum.TerrainViewer.Tests --no-build --filter "FullyQualifiedName~VirgilFollow"
```

## Next

ARC-WORLD-017: carry the non-waiting followers to the arrival cell of a map change and back (slice 4), then orders,
loyalty and the wait timer (slice 5), a walk cycle for NPC bodies, and the session hero as the PC side of the check.
