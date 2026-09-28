# Gamepad prototype evidence

## ARC-GPAD-002 implementation

The Gamepad Lab remains a standalone synthetic Godot 4.7.2 / .NET 8 experiment. It has procedural shapes only: no original data, extracted assets, game integration, original navigation, or controller cursor emulation.

World mode has two independent domains:

- Left stick moves the visible synthetic actor. WASD is the keyboard equivalent.
- Right stick pans the world image/camera. Arrow keys are the keyboard equivalent. The arrow keys navigate menus while a modal is open.
- The fresh and Reset-default settings are `invertCameraX=true` and `invertCameraY=true`: stick-right/down moves the world image right/down, corresponding to camera-left/up. Each settings toggle is independent. Turning both off provides the opposite, non-inverted camera sign using the right stick.
- Reticle targeting, cycling, Talk/Open/Mark/Inspect, Confirm, and Context stay separate from movement. Left-stick motion never emits mouse events, moves an OS cursor, or triggers an interaction.

Camera and actor have individual deadzones (0.05–0.45) and sensitivities (0.5–2.0). Circular deadzone is applied once. Camera pan is delta- and zoom-scaled; actor movement uses a labeled 240 px/s prototype policy and a normalized screen-intuitive desired direction. Camera inversion/zoom do not transform the actor command. The `InputStateReducer` maintains active-device filtering and input-method provenance; focus, disconnect, Reset, modal entry/exit, and cancellation quarantine both sticks and require a complete selected-device neutral sample before rearm.

Settings are versioned JSON in `user://gamepad_lab_settings_v1.json` through the lab-only store. Missing, malformed, or unknown-version settings fall back to the inverted v1 defaults. The HUD shows the actual active values. F2 explicitly recenters the actor; there is no auto-follow camera policy.

## Movement experiment

The default is direct desired-direction/velocity intent. A settings comparison mode uses a bounded actor-relative nearby goal with 96 px lookahead, 0.15 second refresh cadence, 12 px hysteresis, explicit stop/arrival/blocked states, duplicate-target suppression, and cancellation on neutral/blocked/focus/disconnect/modal transitions. A held blocked direction is latched until neutral or a materially changed direction, so it cannot replan every frame. The procedural wall, corner and narrow passage have collision queries; an unreachable attempted goal returns `Blocked`, clears the goal, and does not loop replan.

This is not a pathfinding implementation. It does not route around the synthetic wall/corner/narrow passage. The rationale, observed test fixture paths/stops/replan counts, official 4.7 sources, and limitations are in [gamepad-movement-study.md](gamepad-movement-study.md).

## Verification boundaries

The independent Core harness covers settings default/reset/roundtrip/corrupt fallback, all four inversion combinations, circular deadzone/diagonal/frame delta/zoom behavior, per-device routing, both-stick neutral rearm, focus/disconnect/modal suppression, direct and nearby-goal motion, bounded goal updates, stopping and blocked state. Godot `--self-test` injects scene events and checks actual actor displacement separately from world-image displacement, concurrent sticks, persisted user-local settings, modal/focus clearing, and wall collision. Both are explicitly `SIMULATION` checks.

The local macOS export/smoke is an unsigned, unnotarized prototype; it is not distribution readiness. Physical Xbox Bluetooth axes/buttons, disconnect/reconnect, drift, glyph correctness, and comfort remain **Not verified** until the owner performs the checklist below.

## Owner checklist

1. Connect the Xbox Bluetooth controller and confirm the HUD reports the expected device.
2. In world mode, move the actor with the left stick while panning with the right stick; confirm the actions can occur concurrently.
3. Confirm default right/right and down/down visual camera directions, then independently toggle X and Y inversion in Settings.
4. Check small stick drift on both sticks, focus loss/recovery, disconnect/reconnect, modal opening while either stick is held, cancel, and Reset defaults.
5. Walk the actor into the wall/corner/narrow passage; verify stop/recenter (F2), then compare Direct Intent and Nearby Goal modes.
6. Confirm target cycle, reticle interaction, keyboard/mouse fallback, and that analog movement does not trigger Talk/Attack/Confirm.

References: [controllers](https://docs.godotengine.org/en/4.7/tutorials/inputs/controllers_gamepads_joysticks.html), [NavigationAgent usage](https://docs.godotengine.org/en/4.7/tutorials/navigation/navigation_using_navigationagents.html), [AStarGrid2D](https://docs.godotengine.org/en/4.7/classes/class_astargrid2d.html), [CharacterBody2D](https://docs.godotengine.org/en/4.7/classes/class_characterbody2d.html).