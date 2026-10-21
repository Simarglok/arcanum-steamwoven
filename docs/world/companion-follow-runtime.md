# Companion follow runtime (Virgil follows the PC)

Status: **Implemented bounded component**, ARC-WORLD-016 (slice 3 of the
[companion research](../research/companion-follow-evidence.md#proposed-implementation-slices-project-policy-not-accepted),
ARC-RES-024). After Virgil joins the [session party](party-runtime.md) (ARC-WORLD-013) he walks after the PC in the
`crash-site-area` viewer: Godot-free follow rules in `Arcanum.Simulation.Party`, a second moving actor and the live
actor overlay in Terrain Viewer Core, and the viewer wiring (his dialog now runs through the session party port). It
covers the follow part of [FR-SOCIAL-003](../requirements/story-social-ai.md#fr-social-003) and the waiting part of
[FR-SOCIAL-005](../requirements/story-social-ai.md#fr-social-005) in the viewer. ARC-WORLD-017 (slice 4) carries him
through the cave entrance and exit with the PC ([map change](#map-change-arc-world-017)), the bounded
[FR-WORLD-010](../requirements/world.md#fr-world-010) A→B→A case for one follower.

**NOT READY:** a companion does not walk through living critters outside combat mode, as the original's NPC movers do (the request's flag 0x10, ledger LR2): since ARC-WORLD-074 the PC does, and may stand on a cell a companion wants, while a companion's admission and planning overlays still block on the PC, the other companions and T0 critters (the companions lane's follow-up). No walk cycle or turning for Virgil (his T0 body frame moves with him), running uses the human gait timing
(ARC-WORLD-020, [gait runtime](gait-runtime.md); his own ART stride is not read), only the two cave endpoints carry him (no other map, no
follower of a follower, no unconscious-follower rule, no carried inventory payload), no combat orders, combat or turn-based cost, no
refusal or warning line text (the loyalty step and the wait timer of ARC-WORLD-045 name their line ranges, [below](#loyalty-and-the-wait-timer-arc-world-045)), and the PC side of the follow check is still the AREA diagnostic profile (charisma 8, level 1), not
the session hero. Distances and order are a reading of the original's functions, evidence level F0 (not checked
in the executable) unless the [verification ledger](../research/original-verification-ledger.md) lists them;
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
when the actor settles within `range` of it; a click route uses 0). Its route follower has no corridor limits, so a
leader beyond the local planner's reach is not followed along a corridor as a click goal is (ARC-WORLD-042,
[corridors](click-route-runtime.md#corridors-beyond-the-local-reach-arc-world-042)), and no straight-line retry (the PC's,
ARC-WORLD-073). So the follower walks through the same planner (since ARC-WORLD-073 the original's path request with the
walk's budget), exact movement and step evidence as a click route, re-plans at each step end toward the leader's current
cell, and never cancels a started step. The host calls `Update(state, leader, spreadOut, following, frontier)` once
per frame and then `Drive` to the same frontier:

- **Following** (session standing `Following`): beyond the start distance a goal starts; while it is alive it follows
  the leader's new cell and stops within range. Decisions use the leader cell of that frame, not the source's 100 ms
  re-check.
- **Not following** (waiting or free): the goal stops (`FOLLOW_NOT_FOLLOWING`); a started step ends at its cell centre.
- **Unreachable:** a goal that ends without arriving (`ROUTE_NOT_FOUND`, a denied step) is not planned again until
  the follower or the leader stands on another cell, so a closed-off leader costs one plan, not one per frame.
- **Jump:** beyond 30 cells the update returns the leader's cell; the host stands the follower settled there. On
  the shared cell the next decision steps it to the first Open neighbour in direction order (C10). That choice is
  planned without the leader; if the leader is stepping into that neighbour the step is denied and the next frame
  tries again (transient, no memory).

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

## Map change (ARC-WORLD-017)

Source claims C15, C16 and V4 of the [companion research](../research/companion-follow-evidence.md#findings): a
teleport carries every non-waiting follower, at any distance from the PC, onto the **same** arrival cell; waiting
followers stay on the source map with their leader kept. Both cave scripts teleport with flags 0, so a following
Virgil arrives on (2087,2018) in the cave and on (92913,82598) back outside. On the shared cell the next follow decision
steps him to an Open neighbour (`Separate`, C10). Parity NOT_VERIFIED.

**Where a companion is** (project policy, map-local changes, no snapshot schema change): on its home map (the map of
its T0 record, the exterior for Virgil) unless the home map's entity change `npc.map` names another runtime map; there
its cell is that map's `npc.cell` (absent on the home map = its T0 cell, required on any other map).
`AreaCompanionPlacement.Where` reads it; an unreadable value (an `npc.map` naming the home map, a missing or unpackable
cell) is reported and the companion is skipped, never guessed: on its home map its T0 body is then absent too, so one
identity stands on at most one map. A following companion skipped on the map the PC leaves is not carried.

**The transfer** is one atomic session step: `AreaCompanionGroup.Carry(session, destination, arrival)` lists the
changes (for each following companion: clear its cell on the map it leaves, write the arrival cell on the destination
with its own facing, set `npc.map` to the destination or clear it when the destination is home; every other companion
records where it stands), and `AreaTransitionCoordinator.Process(facing, changes)` applies them with the PC's placement
in one `GameSessionOwner.ApplyChanges` ([session runtime](game-session-runtime.md#api)): one revision, or nothing. A
refusal (for example the change budget) drops the pending transfer with `LastRefusal`; the PC stays and the HUD says
why. Building the templates at load throws on a broken invariant (a body without its visual or frame, a conflicting
frame key), like the other load checks; it is not a runtime failure.

**On each map** the group is rebuilt from the session (`AreaCompanionGroup.Create(templates, …)`):

- `AreaCompanionTemplate` holds a roster companion's home data: its speaker row, its T0 body commands and their
  visuals, its home T0 chart cell. Body commands are shifted from that cell to where the actor stands; the charts share
  one projection, so the same shift stands a visitor on the cave chart. The cave's frame repository gets the
  companions' T0 frames once at load (`AreaCompanionTemplate.Frames`).
- A companion standing on another map is **absent** from its home map: its T0 body is not drawn, picked, named or
  talked to there, and its T0 record does not block (it is in the overlay's moved records).
- A **visitor** walks, follows, waits and blocks like at home. Its body is lit where it stands with its own visual
  (`AreaCompanionGroup.Light`: the original's object colour (F0) at its current cell over the map's tile classes and stationary
  lights); moved companion bodies on the exterior are lit at their current cell the same way. The hover index names and
  picks it where it is drawn, and a click talks to it through the exterior conversation host (its dialog state stays
  on the exterior map). In map mode a companion that the shown map's host does not hold talks through its own host over
  its home map's state (ARC-WORLD-048, [map mode](../rendering/area-map-mode.md#companions-arc-world-037)); the rule is
  the same: one identity, one dialog state, wherever it stands.

## Viewer binding

- `AreaConversationHost(…, party: true)`: a roster speaker talks with `SessionDialogPartyPort` and his measured facts
  (AI row 5 −1000/2001/2001/30, level 1, alignment 1; the diagnostic PC). His `JO 0` joins the session party, `WA`
  and `UW 0 518` run, and node 385 (his follower entry) is no longer a frontier. Other speakers keep the diagnostic
  `follow.pc` key. A refused JO/UW shows `follow refused: <reason>` after the dialog closes.
- The hover/click pick (`AreaHoverIndex.Pick(…, moving)`), talk reach, walk-to-talk, the hover action and the HUD
  prompt use where the companion is drawn and stands now; nothing is picked on his empty T0 cell.
- The PC's click route is planned around the companions as they stand at planning time. When one then steps into it
  and the step is denied by the live overlay (`DESTINATION_ACTOR_OCCUPIED`, `SHOULDER_ACTOR_OCCUPIED`), the same goal
  is planned again, at most 8 times per goal.
- HUD: `companion script 1324 <standing> at (x,y) [visiting] <phase> follow=<action> route=<status> jumps=<n>`,
  `away: script 1324 on map 79` and `skipped: …`; the log prints `ARC-WORLD-016 companion …` step lines and `ARC-WORLD-017
  companions carried {…}` at each transfer.
- ARC-WORLD-017: the transfer carries the following companions (above). In the cave a visiting companion is the only
  dialog NPC; on the exterior an absent one is not a speaker.

## Loyalty and the wait timer (ARC-WORLD-045)

The session's loyalty step and wait timer ([party runtime](party-runtime.md#wait-timer-and-loyalty-arc-world-045)) in the AREA
viewer (exterior, cave and map mode):

- **AI tick.** `AreaCompanionGroup.Loyalty` runs after each frame's companion drive: a following companion's tick is due
  `250 + 4750 × d / 30` ms (integer division, `d` its cell distance to the PC, counted as 30 when above; ledger CL8) after the
  last one, on the frames' simulation time (a clock that went back further than 5 s makes it due at once). A waiting or free
  companion has no tick. No tick runs while a turn-based round is active (the original's step is in the non-combat pass).
- **Facts.** `AreaCompanionLoyalty.Facts`: the companion's follow facts against the diagnostic PC (or the hero, which the session
  substitutes), the shown area's conversation state's PC alignment and the companion's effective reaction under its facts, and its
  level now.
- **Follow-goal gate (decision D5, both settings as the original).** `AreaCompanion.FollowGoal` stands for the original's follow
  goal: an approach (walk, run or jump) starts it, and it lives after the companion arrives, as the original's goal does not end
  by itself (OL19); it ends when the companion stops following, when a conversation with it starts (`InterruptGoals`) and when
  the group is rebuilt (a map change, a quick load, a reset). While it lives the step is `GoalRunning`: in practice the step runs
  before the companion first moves after the PC, and after a talk with it.
- **Alignment.** The conversation state's `AL` arms the followers through `RecordPcAlignmentChange` before it changes the
  diagnostic alignment (a refusal leaves both).
- **Wait timer.** `AreaEventWorld` gives a wait timeout the shown area's conversation state's facts. Since ARC-WORLD-046 the wait
  order (portrait menu) waits a companion with its facts (`AreaCompanionRoster.WaitFacts`); Virgil's wait index 6 sets no timer. The
  dialog `WA` action is not admitted.
- **Orders.** F1–F6 and the follower portrait menu: [companion orders](companion-orders-runtime.md#area-viewer) (ARC-WORLD-046).
- **Log and HUD.** A step that changed the session or was refused prints `ARC-WORLD-045 loyalty script=<id> disposition=… step=…
  consent=… leaveRefused=… warning=… marks=…` with `shown="left, refusal lines 1800-1899 (Dislike)"` or `shown="warning lines
  5400-5499 (LowReaction)"`; a timeout prints `ARC-WORLD-045 wait expired entity=… map=… charisma=… reaction=<skip>:<delta>
  failure=…`. The HUD's companion line names the same ranges or `wait ended for <entity>: jilted (reaction −24)`. No line text is
  loaded.

## Tests

- `tests/Arcanum.Simulation.Tests/Party/CompanionFollowTests.cs`: the distance table, approach to range 2 through the
  route, a moving leader, the unreachable back-off, the jump and the shared-cell step, waiting mid-step, spread out
  and the route range.
- `tests/Arcanum.TerrainViewer.Tests/AreaCompanionTests.cs`: the overlay rule (moved record, destination and shoulder
  actors, precedence, core edge), placement packing, a companion following the PC through the session party and drawn
  on its cell, waiting, the jump, and the placement surviving a save and load.
- `tests/Arcanum.TerrainViewer.Tests/AreaVisitorTalkTests.cs` (ARC-WORLD-048): a visitor's host reads and writes its home map's
  dialog keys and slot, follows through the session party, keeps its state over a save and load, a companion without a kept
  body is reported, and a give-item without the home seed is refused.
- `tests/Arcanum.TerrainViewer.Tests/VirgilFollowOriginalTests.cs` (opt-in `RealData`, `ARC_AREA_ORIGINAL=1` and the
  `ARC_AREA_*` roles): on the authenticated exterior the viewer host joins Virgil through his first talk, node 385
  then opens, and from his T0 chart cell (31,34) he follows a PC at (42,33) to (40,34) in two plans; the cell survives
  a save and load. Development Mac, 2026-10-16.
- ARC-WORLD-017, `AreaCompanionTests` (synthetic, the fixture standing for both maps): a follower carried to the
  arrival cell and back in one revision each, absent at home meanwhile, steps off the shared cell, drawn and lit there,
  survives a save and load; a waiting one stays; unreadable or missing away placements are reported; template frames.
  `tests/Arcanum.Session.Tests/SessionOwnerTests.cs`: `ApplyChanges` atomicity, refusals and the end-state budget.
- `tests/Arcanum.TerrainViewer.Tests/VirgilCaveOriginalTests.cs` (opt-in `RealData`, `ARC_AREA_ORIGINAL=1`, the
  `ARC_AREA_*` roles and `ARC_CAVE_MOBILE`): a following Virgil goes from his T0 cell (31,34) through the entrance onto
  (2087,2018), steps off to an Open neighbour ((2086,2018) in the native smoke), is absent from the exterior meanwhile, and comes back onto (92913,82598)
  through the exit; nothing of him stays in the cave map's changes. Development Mac, 2026-10-16.
- Native smoke `ARC-WORLD-017 native-smoke cave PASS` (after the follow stage, through the production use click):
  Virgil follows into the cave, steps off the arrival cell, is drawn, picked and talked to there; waiting in the cave he
  stays while the PC leaves and the exterior has no T0 Virgil; following again he comes back out and steps off next to
  the PC. `ARC_AREA_CAVE_FOLLOW_CAPTURE=<png>` saves the cave frame.
- `tests/Arcanum.TerrainViewer.Tests/AreaCompanionLoyaltyTests.cs` (ARC-WORLD-045): the AI tick delay, the step on a following
  companion's tick and its schedule, the follow-goal gate and a talk ending it, a refused check making it leave, the approach
  starting the goal and waiting ending it, a wait timer jilting through the event pump with the area's reaction facts, and `AL`
  arming the followers.
- Native smoke (`--native-smoke --mode crash-site-area` with the original roles): `ARC-WORLD-045 native-smoke loyalty PASS`
  inside the follow stage (a reaction drop arms Virgil, his AI tick clears the marks while he stands near the PC with no follow
  goal, and after he followed the PC the step stays gated; his wait sets no timer), then `ARC-WORLD-016 native-smoke follow
  PASS` after the quick-save stage: Virgil joins through his dialog in the running viewer, follows the PC to within 2,
  is drawn on his new cell only, his cell is in the session, and a waiting Virgil stays while the PC is farther than 4.
  `ARC_AREA_FOLLOW_CAPTURE=<png>` saves the frame.

```sh
"$DOTNET" test tests/Arcanum.Simulation.Tests --no-build --filter "FullyQualifiedName~CompanionFollow"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests --no-build --filter "FullyQualifiedName~AreaCompanion"
ARC_AREA_ORIGINAL=1 ARC_AREA_MODULE=… ARC_AREA_RESOURCE1=… ARC_AREA_RESOURCE2=… ARC_AREA_MOBILE=… ARC_AREA_PROTOTYPES=… \
  ARC_AREA_EFFECTS=… ARC_AREA_PATCH=… "$DOTNET" test tests/Arcanum.TerrainViewer.Tests --no-build --filter "FullyQualifiedName~VirgilFollow"
ARC_AREA_ORIGINAL=1 ARC_CAVE_MOBILE=… ARC_AREA_MODULE=… (same roles) \
  "$DOTNET" test tests/Arcanum.TerrainViewer.Tests --no-build --filter "FullyQualifiedName~VirgilCave"
```

## Any map (ARC-WORLD-037)

In the viewer's map mode every dialog NPC with follow facts from its own data is a possible companion, and followers
travel with the PC between maps by `AreaCompanionGroup.Arrive`
([companions in map mode](../rendering/area-map-mode.md#companions-arc-world-037)). The exterior and cave keep the
measured roster.

## Next

The combat orders (stage A7; the out-of-combat orders are ARC-WORLD-046, [companion orders](companion-orders-runtime.md), the
loyalty step and wait timer ARC-WORLD-045), a walk cycle for NPC bodies, the session hero as the PC side of the
check, and the rest of the general map-change carry (followers of followers, unconscious followers, carried
inventory).
