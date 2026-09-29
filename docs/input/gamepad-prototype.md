# Gamepad prototype evidence

## ARC-GPAD-002 implementation

The Gamepad Lab remains a standalone synthetic Godot 4.7.2 / .NET 8 experiment. It has procedural shapes only: no original data, extracted assets, game integration, original navigation, or controller cursor emulation.

World mode has two independent domains:

- Left stick moves the visible synthetic actor. WASD is the keyboard equivalent.
- The camera initially follows the synthetic actor, clamped to the synthetic map. Right stick pans the world image/camera and immediately enters Manual; arrow keys are the keyboard equivalent. The arrow keys navigate menus while a modal is open.
- The fresh and Reset-default settings are `invertCameraX=true` and `invertCameraY=true`: stick-right/down moves the world image right/down, corresponding to camera-left/up. The characterized GPAD001 baseline also translated the image right/down for positive pan. The owner explicitly superseded the former false/false baseline clause: `true` retains this requested visual sign and turning an axis `false` reverses only that axis. Each settings toggle is independent; the HUD states the active right/down image directions. This camera choice never restores left-stick camera control.
- Reticle targeting, cycling, Talk/Open/Mark/Inspect, Confirm, and Context stay separate from movement. Left-stick motion never emits mouse events, moves an OS cursor, or triggers an interaction.

Camera and actor have individual deadzones (0.05–0.45) and sensitivities (0.5–2.0). Circular deadzone is applied once. Camera pan is delta- and zoom-scaled; actor movement uses a labeled 240 px/s prototype policy with linear analog effort end-to-end (full input: 0.5 = 120 px/s; 2.0 = 480 px/s). Direction remains normalized and screen-intuitive; camera inversion/zoom do not transform the actor command. Manual persists through a right release while left remains held and through the first neutral frame. Both post-deadzone domains must be neutral to arm recenter; a later actor-only edge returns to Follow, while a right/simultaneous edge retains Manual. Follow is a direct, bounded center (no smoothing/overshoot); any pan interrupts it. The `InputStateReducer` maintains active-device filtering and input-method provenance; focus, disconnect, Reset, modal entry/exit, and cancellation quarantine both sticks, clear camera recenter edges, and require a complete selected-device neutral sample before rearm without moving the preserved view on reconnect.

Settings are versioned JSON in `user://gamepad_lab_settings_v1.json` through the lab-only store. Missing, malformed, or unknown-version settings fall back to the inverted v1 defaults. The HUD shows Follow/Manual status, camera center, and neutral-arm state. F2 explicitly recenters actor and camera into Follow; Reset defaults does the same. The HUD reticle/semantic target system remains viewport-based and independent of actor locomotion, although a Follow camera naturally changes the world presented around it. Synthetic camera bounds handle viewport/zoom and map-smaller-than-viewport midpoint behavior; they make no claim about original Arcanum bounds.

## Movement experiment

The default is direct desired-direction/velocity intent. A settings comparison mode stores a bounded actor-relative 96 px semantic/debug destination, steers toward it, and refreshes it only after the 0.15 second cadence and 12 px hysteresis gates open. Bounded internal progression preserves 0.5/2.0's 4:1 open displacement ratio across long or split frames while retaining a real `Arrived` stop state and a visible destination. Neutral/blocked/focus/disconnect/modal transitions cancel it. Both modes latch a held blocked direction until neutral or a materially changed direction; Nearby Goal additionally clears its destination and therefore cannot replan every frame. The procedural wall, corner and narrow passage use a bounded 8 px synthetic occupancy sweep; an unreachable attempted goal returns `Blocked`, retains the last safe position, and does not tunnel or loop replan. This is not general collision/pathfinding evidence.

This is not a pathfinding implementation. It does not route around the synthetic wall/corner/narrow passage. The rationale, observed test fixture paths/stops/replan counts, official 4.7 sources, and limitations are in [gamepad-movement-study.md](gamepad-movement-study.md).

## Verification boundaries

The independent Core harness characterizes the historical GPAD001 image-right/down sign, then covers all four owner-confirmed inversion combinations, settings default/reset/roundtrip/corrupt fallback, circular deadzone/diagonal/frame delta/zoom behavior, per-device routing, both-stick neutral rearm, focus/disconnect/modal suppression, linear 0.5/2.0 actor effort in both modes, default-policy long-vs-split Nearby Goal movement, bounded goal updates, stopping, blocked state, and swept thin-obstacle collision. Godot `--self-test` injects scene events and checks all four actual world-image camera signs plus actual actor displacement separately from world-image displacement, concurrent sticks, responsive target/ground projection, controller arbitration/provenance including rendered diagnostics, modal repeat, persisted settings, lifecycle clearing, long-vs-split sensitivity ratios, and actual wall/corner/far-side narrow-passage traversal. The exported validation additionally proves a non-default write, a fresh-process reload, reset, and a second fresh-process inverted-default reload using an isolated temporary lab-settings file that is deleted even when validation fails; ordinary user preferences are neither read nor replaced. Both are explicitly `SIMULATION` checks.

The local macOS export/smoke is an unsigned, unnotarized prototype; it is not distribution readiness. Physical Xbox Bluetooth axes/buttons, disconnect/reconnect, drift, glyph correctness, and comfort remain **Not verified** until the owner performs the checklist below.

## Owner checklist

1. Connect the Xbox Bluetooth controller and confirm the HUD reports the expected device.
2. In world mode, move the actor with the left stick while panning with the right stick; confirm the actions can occur concurrently.
3. Confirm default right/right and down/down visual camera directions, then independently toggle X and Y inversion in Settings.
4. Check small stick drift on both sticks, focus loss/recovery, disconnect/reconnect, modal opening while either stick is held, cancel, and Reset defaults.
5. Walk the actor into the wall/corner/narrow passage; verify stop/recenter (F2), then compare Direct Intent and Nearby Goal modes.
6. Confirm target cycle, reticle interaction, keyboard/mouse fallback, and that analog movement does not trigger Talk/Attack/Confirm.

References: [controllers](https://docs.godotengine.org/en/4.7/tutorials/inputs/controllers_gamepads_joysticks.html), [NavigationAgent usage](https://docs.godotengine.org/en/4.7/tutorials/navigation/navigation_using_navigationagents.html), [AStarGrid2D](https://docs.godotengine.org/en/4.7/classes/class_astargrid2d.html), [CharacterBody2D](https://docs.godotengine.org/en/4.7/classes/class_characterbody2d.html).