# Gamepad prototype evidence

## Implemented hypothesis

ARC-GPAD-001 is an isolated synthetic Godot input experiment. The fixed center reticle selects one visible target anchor projected to logical viewport coordinates. The policy is radius 180 px, Euclidean score with zero type biases, epsilon 0.001 px then ordinal ID ties, 24 px automatic hysteresis, and explicit stable cycling. Manual cycle/mouse selection locks while eligible; B/cancel returns to automatic selection. A mouse miss preserves its clicked world-ground coordinate; otherwise no candidate yields exact center-reticle Ground fallback. Confirm logs a synthetic destination and inspect logs synthetic coordinates.

All world objects are procedural labeled primitives: two NPCs, two enemies, two doors, crate and adjacent tiny gear, loose vial, walls/pillars, isolated object and a foreground pillar. It makes no visibility/occlusion claim. Interaction only changes synthetic log/state: Talk, Open, Pick up, Mark, Choose destination, or Inspect. It expressly does not implement dialogue, inventory, combat, movement, pathfinding, party/follow/direct-character/radial modes, rumble, gyro, touchpad, Steam Input, other-platform certification, original assets, or viewer integration.

## Input and coordinates

`lab_*` InputMap actions declare keyboard and SDL Godot events locally, including both left-stick directions for pan and menu navigation. The adapter maps keyboard/mouse and SDL Godot controller enums to semantic intents and samples held keyboard/stick state continuously; it does not use aggregated controller `Input.IsActionPressed`. Joy events must match the selected active device. Startup chooses lowest session ID. When no controller is active, meaningful events are collected for one frame and the lowest candidate is selected; non-active connects remain diagnostic-only and cannot interrupt an active controller. `JoyConnectionChanged` clears levels/repeat state only on active-device disconnect; reconnect, focus and reset require a neutral selected-device frame before arming. No device is trusted by an old numeric ID. A reconnect candidate must produce one complete neutral selected-device frame (A/B/X/Y/Start/D-pad/shoulders released; left stick within the circular deadzone), then arms on the following frame.

Deadzone is applied once: `m <= d => 0`, otherwise `normalize(v) * clamp((m-d)/(1-d), 0, 1)`. Defaults/ranges are d=0.20 (0.05–0.45) and sensitivity=1.0 (0.5–2.0). Camera screen speed is 520×sensitivity logical px/s and displacement divides by Camera2D zoom. `Camera2D` is outside `WorldRoot`, so continuous keyboard/controller world translation produces actual relative canvas displacement rather than moving camera and content together. Keyboard direction is normalized. Meaningful controller switching requires a digital press or processed stick >=0.25 for 100ms; keyboard/mouse requires non-echo press/button or 6 logical px in 150ms. Prompts change only; semantic selection remains.

Modal UI gets semantic ownership first; it clears held world levels and suppresses world pan/pulses. Directional menus move immediately then repeat after .35s and at .10s, with analog .65 engage/.45 release and one move per update. Context list is primary action, Inspect, Cancel; settings provides deadzone/sensitivity adjustments, reset and close through keyboard, mouse or accepted controller intent. Selected objects receive a visible gold outline. Mouse left-picks top-drawn visible targets (or ground) into a manual lock; it is an adapter fallback, never a controller cursor emulation.

## Reproducible checks and artifacts

Run the locked restore, Release build, Core harness, editor import, and `--self-test` commands in `experiments/gamepad-lab/README.md`. `--smoke <json>` writes explicit `SIMULATION` output after scene/selection/confirm/inspect/settings-reset exercise. `--capture WxH <png>` creates rendering/layout evidence at 1280×800 and 1920×1080. The ignored artifact manifest records SHA, toolchain, command evidence, files, captures and the `current.json` pointer.

| Acceptance area | evidence |
|---|---|
| Core math/input/device/menu/targeting | dependency-free `Tests` executable harness |
| Actual scene and mappings | Godot `--self-test`, explicitly `SIMULATION` |
| launch/selection/settings | exported-app `--smoke`, explicitly `SIMULATION` |
| readable layouts | exported `--capture` PNG dimension checks at 1280×800 and 1920×1080 |
| physical device evidence | Bluetooth axes/buttons, reconnect, drift, glyph correctness and comfort: **Not verified** |

The local macOS app is unsigned and unnotarized. Manual owner checklist: connect controller; fine/coarse pan; crowded selection and crate→tiny gear; inspect/interact; context/settings navigation/cancel; disconnect/reconnect; focus transitions; keyboard/mouse/gamepad switching. References: [Godot Input](https://docs.godotengine.org/en/4.7/classes/class_input.html), [InputMap](https://docs.godotengine.org/en/4.7/classes/class_inputmap.html), [controllers](https://docs.godotengine.org/en/4.7/tutorials/inputs/controllers_gamepads_joysticks.html), [Camera2D](https://docs.godotengine.org/en/4.7/classes/class_camera2d.html), [Node](https://docs.godotengine.org/en/4.7/classes/class_node.html), [macOS export](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html).
