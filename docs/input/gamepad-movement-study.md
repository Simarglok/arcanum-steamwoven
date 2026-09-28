# Gamepad movement study — synthetic lab only

Date: 2026-09-28. Scope: ARC-GPAD-002's procedural Godot 4.7.2 lab. This is not a finding about original Arcanum controls, navigation, terrain, or compatibility.

## Question

The owner's click-and-hold description is treated as an input-preference hypothesis: can analog motion be expressed as a nearby actor-relative destination? It does not establish original movement semantics. The lab compares three boundaries:

| Option | Result in this lab | Decision |
|---|---|---|
| A. Emit synthetic mouse presses/motion | Not implemented as a movement adapter. It would conflate controller intent with targeting/interaction, depend on reticle/canvas transforms, and reintroduce click side effects. | Rejected for production/lab default. |
| B. Refresh actor-relative nearby semantic goal | Implemented as the inexpensive comparison mode. It stores a bounded 96 px actor-relative destination, steers toward that destination, and refreshes only when its 0.15 s cadence and 12 px hysteresis gates permit it. A bounded internal progression preserves open-space displacement across frame partitions without treating the target as a live direction marker. It cancels on neutral or blocked state, records bounded refreshes, and latches a held blocked direction until neutral or a materially changed direction. | Retained for comparison. |
| C. Direct desired direction / velocity intent | Implemented as the default. Input is semantic direction plus analog effort; the replaceable scene adapter applies the explicit 240 px/s prototype policy and collision query. | Provisional default. |

## Observed reproducible comparison

`Tests/GamepadLab.Tests.csproj` runs the engine-independent comparison. With a speed of 100 px/s used only by the test fixture and a 40 px nearby lookahead:

| Fixture | Direct result | Nearby-goal result |
|---|---|---|
| Open straight segment, 0.1 s | position advances 10 px | position advances 10 px toward the stored 40 px target; replan count=1 |
| Same held direction, a further 0.05 s | direct continues | actor advances toward the stored target, then refreshes once (replan count=2) because both cadence and actor-relative hysteresis have opened |
| Direction change before/after cadence | changes immediately | continues toward the stored target before the gate; after cadence/hysteresis, refreshes to the new actor-relative target and turns toward it |
| Stop distance | not applicable to a continuously direct command | an already-near stored target returns explicit `Arrived` and holds until a gated refresh or neutral |
| Wall at x=20 from x=15 | attempted step blocks | same bounded `Blocked` outcome; goal is cleared rather than queued for replan |
| Held direction after `Blocked` | no new motion attempt; neutral or a materially redirected intent is required before retry | no new motion attempt or replan; neutral or a materially redirected intent is required before retry |
| Neutral input | no pending path state | goal cleared and outcome `Idle` |

The default-policy Core and actual-scene checks compare one 0.5 s frame with five 0.1 s frames at 0.5 and 2.0 effort: both Nearby Goal paths preserve equal open displacement and the 4:1 high/low ratio. The target is real movement state rather than a presentation-only marker; bounded internal slices progress toward it and process cadence/hysteresis deterministically. The rendered synthetic courtyard provides a wall, a corner, and a narrow passage. The Godot `--self-test` drives the actual left-stick actor adapter through each fixture in both modes: a bounded synthetic occupancy sweep samples intermediate positions no farther than 8 px apart, so a large delta cannot cross the thin procedural wall/corner merely because its final endpoint is clear. Both modes traverse the centered narrow passage beyond its far edge. This is evidence only for this callback/fixture, not general physics or navigation. Neither adapter routes around obstacles: direct intent stops, and the nearby-goal comparison stops/cancels. That limitation is deliberate; no large navigation stack was added.

## Rationale

Direct intent is the safer provisional lab behavior because it has no path recomputation jitter, no arrival target that can lag behind a continuously held stick, and no dependency on canvas/zoom/isometric presentation transforms. It also gives the future independent simulation a semantic direction boundary rather than an OS event. The actor's screen-intuitive direction is passed independently of the camera; camera inversion and zoom never rotate or scale it. Actor sensitivity is an explicit linear prototype effort multiplier on 240 px/s: full input at 0.5 is 120 px/s and at 2.0 is 480 px/s; it is not a claim about original walk/run rules.

The nearby-goal mode remains useful as a small experiment because it exercises the owner's hypothesis without using generated mouse events. Its cadence, hysteresis, visible stored actor-relative marker, explicit `Arrived`/`Blocked` results, and neutral cancellation make its state bounded without making open movement frame-partition dependent. It is not a pathfinding claim. A future navigation experiment must separately define obstacle avoidance, corner behavior, arrival tolerance, and authority boundaries.

## Godot 4.7 references and limits

The following official 4.7 URLs returned HTTP 200 when checked on 2026-09-28; they are version-pinned references, not copied implementation:

- https://docs.godotengine.org/en/4.7/tutorials/inputs/controllers_gamepads_joysticks.html
- https://docs.godotengine.org/en/4.7/tutorials/navigation/navigation_using_navigationagents.html
- https://docs.godotengine.org/en/4.7/classes/class_astargrid2d.html
- https://docs.godotengine.org/en/4.7/classes/class_characterbody2d.html

Navigation agents calculate/follow navigation paths but do not themselves move actors; repeated target replacement would therefore require explicit cadence and jitter controls. `AStarGrid2D` could support a later synthetic grid comparison. `CharacterBody2D` could provide a later Godot physics adapter. Neither proves original Arcanum behavior, and neither is authoritative in this lab: `Core/Movement.cs` remains engine independent and collision is a replaceable adapter callback.

## Remaining uncertainty

No physical Xbox Bluetooth session was used for this build. No claim is made about drift, controller mapping quality, comfort, analog walk/run scaling, real-game collision, original navigation, party behavior, combat, or terrain. The local app labels all injected checks `SIMULATION`.