# Companion follow runtime (Virgil follows the PC)

Status: **Implemented bounded component**, ARC-WORLD-016 (slice 3 of the
[companion research](../research/companion-follow-evidence.md#proposed-implementation-slices-project-policy-not-accepted),
ARC-RES-024). After Virgil joins the [session party](party-runtime.md) (ARC-WORLD-013) he walks after the PC in the
`area` viewer: Godot-free follow rules in `Arcanum.Simulation.Party`, a second moving actor and the live
actor overlay in Terrain Viewer Core, and the viewer wiring (his dialog now runs through the session party port). It
covers the follow part of [FR-SOCIAL-003](../requirements/story-social-ai.md#fr-social-003) and the waiting part of
[FR-SOCIAL-005](../requirements/story-social-ai.md#fr-social-005) in the viewer. ARC-WORLD-017 (slice 4) carries him
through the cave entrance and exit with the PC ([map change](#map-change-arc-world-017)), the bounded
[FR-WORLD-010](../requirements/world.md#fr-world-010) A→B→A case for one follower.

**Critters (ARC-WORLD-075):** outside combat mode a companion walks through living critters, as the original's follower does (its move-near and run-near legs plan with the request's flag 0x10, ledger LR10, and the PC's walk does too since ARC-WORLD-074): `AreaCompanion.PassesCritters` is read from the session at each drive (the companion's own combat mode), and while it holds the companion's plan (`AreaRouteSource` `crittersPass`) and its steps (`AreaGridSession.CrittersPass`) pass T0 PCs and NPCs, the PC and the other companions; other objects still block, and a living critter on the goal still refuses it (DC2). In combat mode they block it as before; nothing in the viewer puts a companion in combat mode yet (only the hero's R key sets combat mode), so Virgil passes critters also while the hero is in combat mode, where the PC is blocked by him. Under the Original rule mode (`SegmentEndCritterEndsWalk`, LR11) a critter before a segment's last step stops the goal and the next update goes on approaching from where the companion stands (beyond the range it plans again, even within the start distance), so it pauses one frame and walks on (the original's 2 action points and random 0..300 ms wait are not modelled); Steamwoven walks on at once. An ordered goal stopped there ends, as the walk-here order's goal 4 does (LR5); the spread-out step would plan again in the original (goal 69). NOT READY: a companion may end its approach on a T0 NPC's or another companion's cell and stay there, since it separates only from the leader's cell (distance 0); the original's shared-cell step-away (C10) is not ported. No walk cycle or turning for Virgil (his T0 body frame moves with him), running uses the human gait timing
(ARC-WORLD-020, [gait runtime](gait-runtime.md); his own ART stride is not read), only the two cave endpoints carry him (no other map, no
follower of a follower, no unconscious-follower rule, no carried inventory payload), no combat orders, combat or turn-based cost, no
refusal or warning line text (the loyalty step and the wait timer of ARC-WORLD-045 name their line ranges, [below](#loyalty-and-the-wait-timer-arc-world-045)). Since ARC-WORLD-067 the PC side of the follow check, the orders, the loyalty step and the wait timer is the
session hero with `--hero` ([party runtime](party-runtime.md#the-session-hero-as-the-pc-side-arc-world-067)); without one the
labelled AREA diagnostic profile (charisma 8, level 1, no Persuasion) stands in. Distances and order are a reading of the original's functions, evidence level F0 (not checked
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
  the follower or the leader stands on another cell, so a closed-off leader costs one plan, not one per frame. An
  approach stopped before a living critter at a segment's end (`ROUTE_SEGMENT_END_CRITTER`, Original, ARC-WORLD-075) is
  not unreachable: it goes on approaching at the next update.
- **Jump:** beyond 30 cells the update returns the leader's cell; the host stands the follower settled there. On
  the shared cell the next decision steps it to the first Open neighbour in direction order that holds no living
  critter (a project rule; the original's shared-cell step-away C10 is not ported). That choice is planned without the
  leader; outside combat mode its step passes the leader (ARC-WORLD-075), in combat mode a step into the cell the leader
  is entering is denied and the next frame tries again (transient, no memory).

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
verdict the step was admitted with, when the overlay changed it. The PC is admitted against the companions in combat mode (since ARC-WORLD-074 outside it the PC's steps pass them and every living critter, [click route](click-route-runtime.md#follower-routefollower)); each
companion against the PC and the other companions in combat mode (since ARC-WORLD-075 outside it its plan and steps pass them
and T0 critters, `AreaCompanion.PassesCritters`), except that a companion **plans** toward the PC without the PC's
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

The original moves the follower as a whole object of the destination map (no visitor mode); the gap between that and the
visitor model below, and the fix plan, are in [followers across maps](../research/follower-map-transfer.md) (ARC-RES-076).

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

**Parity of the move (ARC-DEV-073, migration step MIG10 of the [target model](../architecture/target-model.md); research
[FM1, FM2, FM6](../research/follower-map-transfer.md), RL22, RL23, RL28).** The transfer asks the session for a move list
first (`GameSessionOwner.QueryTransferMoveList(SessionTransferFacts)` over `TransferMoveListRules`): unless the request
carries the "no wait orders" flag (0x100, teleport spells'; the cave and map exits do not set it), every direct follower for
which the original's 0x45D800 answers yes (not undead and no fatigue left, so the unconscious, not the dead) is ordered to
wait, and the others travel, each after its own followers. `Carry`/`Leaving` give the companions that go; an unconscious
follower stays where it stands, its wait timer (OL23) parked on the map the PC leaves. The list goes into the same
`ApplyChanges(expected, actor, changes, moveList)`, in the same revision: the wait orders, FM6's clearing of focus, who hit
me last, the enemy list and fleeing-from for every follower that travels (the danger state bits stay; the substitute
inventory is a barter store the session does not keep per critter), and, on another map, the travellers' pending events
including their own AI tick, which move with them instead of the tick being dropped (the destination's first AI start finds a
pending tick and leaves it, so there is one). The viewer takes the travelling followers out of combat mode on another map
(the toggle's own refusals apply). A list that does not match the party is `InvalidCompanion`, one whose timers do not fit
`EventBudgetExceeded`; either applies nothing. **NOT READY:** the session models no NPC leader, so no follower has followers
of its own and the walk below a follower finds none (the rule is tested with an injected relation); the carried tick keeps
its old deadline instead of being replaced by the fold's first tick with the wake-up marker (TM5, MIG9); inventories are not
moved (the stores are identity-keyed). On a map load the list and the followers' combat-mode exit are taken when the move is requested, so a later refusal of the arrival (a follower's standing changed meanwhile) leaves the PC on the old map with the followers already out of combat mode, as the PC's own exit already does.

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
  on the exterior map). In map mode a companion that the shown map's host does not hold is listed by that host as one more speaker (ARC-DEV-081, MIG5) and talks over
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

## Drawing the walk (ARC-WORLD-111)

A playtest of the packaged build showed Virgil sliding after the PC as a still picture: until now a companion (and an NPC
the AI walks, [AI host](ai-host.md)) was drawn with its T0 frame moved along. The original draws every walker's step the
same way, PC or NPC: the step gait (0x42EDC0, read in the executable for any critter, ledger
[ST3](../research/ledger/skills.md)) sets the walk (animation 1) or run (6), the stealth walk (3) for a concealed one; the
frames run at the ART rate the Speed table scales (research [ARC-RES-047](../research/locomotion-evidence.md) L5–L7, F0);
facing selects the ART rotation; a stand is animation 0 frame 0 (ST1, and the end of a move,
[actor placement](../research/actor-placement-evidence.md), F0). This is a technical bug of ours, fixed in both rule modes.

- **Move ARTs** (`AreaBodyMoveArt.Load`, viewer Core): read on demand from the archive the body's standing art came from,
  once per standing art (dropped with the map's frame overlay at a map change or reload), its archive entry checked
  against the published snapshot (a changed archive is refused); bytes after an ART's last frame are not part of it. The
  viewer keeps the archives it read open, with their file records by name, across maps and map windows
  (`AreaArtArchives`, ARC-RENDER-3414), so a window swap does not parse an archive again for every body; an archive whose
  length or write time changed, or whose read failed, is opened again. The
  names are the body's ART name with new shield, weapon and animation letters (ledger MA3): walk `x<weapon>b` (walk
  clears the shield, MA2), run `xag` (animations 6–19 clear both), stand `<shield><weapon>a`. Missing-art repair (MA1):
  a missing run is drawn as the walk, a missing armed walk as the walk without the weapon (`xab`), a missing
  stand with weapon 0 as weapon 1; an unreadable candidate leaves the next one. Critter, monster and unique-NPC art only (classes 2, 12, 13), not bad art; any
  other status keeps the T0 frame and the log says why once (`ARC-WORLD-111 body-moves …`).
- **Facing and placement** (`AreaBodyArt.Pose`, `AreaBodyMoveArt.Animate`): facing octant = ART rotation; rotations 1..3
  draw groups 7..5 mirrored with hotspot X `width − hotX − 1` (AP-006), a one-rotation ART serves every facing from its
  group 0 (mirrored for 1..3, as the T0 body). The
  frame is placed by `AreaScenePlan.Repose` at the body's anchor where it stands now; the object's own flip stays. The
  frames join the shown map's frame overlay, as the fallen poses do.
- **Clock** (`AreaBodyCycle`): as the PC's, a walk's steps share one cycle; it restarts when the walker starts moving or
  changes gait. Frame = elapsed ms × ART fps ÷ 1000 mod frames, on the viewer's world clock (the clock the walks run on).
  A companion: its grid's drawn action and facing (`DiagnosticGridActor.RenderState`), its clock `AreaCompanion.Cycle`
  living with its group; until it first moves it keeps its T0 frame and facing. An AI mover: walking or running while
  its step lasts (`AreaAiMover.Stepping`, `Running` from the run's gait), else its stand frame turned as it faces (a
  turn in place is drawn at once); until it first moves, standing with its T0 facing keeps its T0 frame. Once moved, a
  body always stands on its stand ART: the T0 frame of a mirrored critter is placed by its unmirrored hotspot (the T0
  plan's rule), and going back to it would shift the body sideways.
- **Bounded.** The frame rate is the ART's own (the Speed table's middle rate, as the PC at Speed 8); the sprite moves
  linearly across the step, not by the ART's frame offsets; the vault, stealth walk, attack, cast and other NPC
  animations are not drawn (a follower's window step shows the walk); no footstep sounds for NPCs. The move ARTs are
  looked up in the standing art's archive only, not through the resource order (a loose file or a later archive that
  overrides one is NOT READY). Original UI parity NOT VERIFIED.

## Loyalty and the wait timer (ARC-WORLD-045)

The session's loyalty step and wait timer ([party runtime](party-runtime.md#wait-timer-and-loyalty-arc-world-045)) in the AREA
viewer (exterior, cave and map mode):

- **AI tick.** `AreaCompanionGroup.Loyalty` runs after each frame's companion drive: a following companion's tick is due
  `250 + 4750 × d / 30` ms (integer division, `d` its cell distance to the PC, counted as 30 when above; ledger CL8) after the
  last one, on the frames' simulation time (a clock that went back further than 5 s makes it due at once). A waiting or free
  companion has no tick. No tick runs while a turn-based round is active (the original's step is in the non-combat pass).
- **Facts.** `AreaCompanionLoyalty.Facts`: the companion's follow facts against the PC side (the session hero, ARC-WORLD-067, else
  the diagnostic PC with the shown area's conversation state's alignment), the companion's effective reaction under its facts, and
  its level now.
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
- `tests/Arcanum.TerrainViewer.Tests/AreaBodyMoveTests.cs` (ARC-WORLD-111): the move ART names and their missing-art
  repair, the refusals, a facing's group and its mirrored hotspot, a walking body's cycle frame at its anchor, the stand
  frame of a turned body, the gait clock and an AI mover's gait. Native smoke: `ARC-WORLD-111 native-smoke follower walk
  PASS` (Virgil's walk drawn with ≥ 3 distinct frames of his walk cycle and never his T0 frame while he walks, inside the
  ARC-WORLD-016 follow stage) and the `ai` map stage's NPC steps drawn with its move cycle (`StepFrames` in
  `ARC-WORLD-092 native-smoke ai PASS`).
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
- Native smoke (`--native-smoke --mode area` with the original roles): `ARC-WORLD-045 native-smoke loyalty PASS`
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
loyalty step and wait timer ARC-WORLD-045), and the rest of the general map-change carry (followers of followers, unconscious followers, carried
inventory).
