# Synthetic Gamepad Input Lab

Standalone Godot 4.7.2 / .NET 8 experiment for camera-centered, semantic gamepad input. It contains procedural primitives only: **no original game data, extracted content, third-party assets, or product integration**.

## Hypothesis and controls

The center reticle is the focus. The lab evaluates visible interaction anchors in logical viewport pixels inside a 180 px radius, scores Euclidean distance (zero kind bias), breaks 0.001 px ties by ordinal ID, retains automatic selection within 24 px hysteresis, and cycles immutable `CycleOrder` (crate immediately before tiny gear). An offscreen, invalid, removed, or out-of-radius target is ineligible. With no eligible target, the exact reticle center is Ground; Ground is excluded from cycling.

| Intent | keyboard/mouse | controller |
|---|---|---|
| Pan | WASD/arrows | SDL left stick |
| Confirm / cancel | Enter/Space / Escape | A / B |
| Inspect | I | X |
| Target cycle | Q / E | LB / RB |
| Context / settings | C / F1 | Y / Start |
| Menu directions | arrows/WASD | D-pad / left stick |

InputMap declares the keyboard and SDL semantic actions (including both left-stick directions); the adapter maintains per-device event state and accepts joy events only from the deterministic active device. With no active device, a frame collects meaningful controller candidates and selects the lowest ID; additional connected controllers remain diagnostic-only. Keyboard and active-controller samples remain separate: held levels continue routing pan/menu semantics but only a non-echo keyboard/mouse press, controller rising edge (including D-pad), or qualified stick motion changes the input method. This prevents held opposite-device levels from oscillating prompts. It samples held stick/keyboard pan every frame, so panning is continuous and frame-rate/zoom scaled. `Camera2D` is intentionally outside the translated WorldRoot, while the fixed reticle is drawn by the CanvasLayer HUD in viewport space. Keyboard/mouse switching is immediate for a non-echo press/button, while pointer travel accumulates to 6 logical pixels within a 150 ms frame-clock window; expiry, quarantine, and a successful switch reset that window. Active-controller disconnect and focus transitions clear held/repeat state; when a controller remains selected, keyboard/no-device frames cannot rearm it: reconnect/focus/reset require a complete neutral selected-controller sample and a following arm frame. Circular deadzone defaults to 0.20 (0.05–0.45); sensitivity defaults to 1.0 (0.5–2.0). Prompts are method-appropriate (Xbox semantic glyphs or keyboard/mouse alternatives), while diagnostics identify the concrete raw button/axis/key event, its accepted/rejected state, and provenance. The app labels engine delivery `LIVE OS EVENT (not physical usability proof)` and all injected automation `SIMULATION`.

## Build and checks

Use the canonical process-local SDK and Godot executable:

    export DOTNET_ROOT="$HOME/.local/share/dotnet"
    export PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin"
    export __GODOT_SHELL_ENV_SET=1
    "$DOTNET_ROOT/dotnet" restore GamepadLab.csproj --locked-mode
    "$DOTNET_ROOT/dotnet" restore Tests/GamepadLab.Tests.csproj --locked-mode
    "$DOTNET_ROOT/dotnet" build GamepadLab.csproj -c Release --no-restore
    "$DOTNET_ROOT/dotnet" run --project Tests/GamepadLab.Tests.csproj -c Release --no-restore -- --all
    /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --editor --path . --quit
    "$DOTNET_ROOT/dotnet" build GamepadLab.csproj -c Debug --no-restore
    /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . -- --self-test

The Core executable harness covers radial deadzone/no drift, normalization and delta/zoom scaling, same-frame lowest-device arbitration/quarantine, cumulative 6 px/150 ms mouse switching (including expiry, jitter, reset, and held-level non-oscillation), every rising-edge pulse, focus/reset stale-edge suppression, modal world suppression, deterministic digital/analog menu repeat, epsilon ties/exact hysteresis boundary, bidirectional cycle wrap/cancel-to-auto/lock invalidation/removal/out-of-radius, offscreen/world-ground fallback, and projection fixtures at 1280×800 and 1920×1080. After a fresh Debug build, Godot self-test drives actual scene/adapter events: controller arbitration and held delta/zoom pan with canvas displacement, method-specific prompt/accepted-event diagnostics, target/ground projection at both sizes and zooms, digital and analog modal repeat with world suppression, focus/disconnect pulse/log clearing, and responsive in-bounds/non-overlapping HUD rectangles. This is explicit **SIMULATION** provenance, not physical-device evidence.

## Evidence limits and owner checklist

Export/capture proves a local unsigned/unnotarized macOS prototype, not distribution readiness. Preserve every capture launch command, candidate SHA/UUID, timestamps, timeout/exit result, and crash correlation in evidence; a saved PNG is not proof that its process exited cleanly. `--smoke` emits `physicalDeviceEvidence` as an object with `xboxBluetoothAxesButtons`, `disconnectReconnect`, `drift`, and `comfort`, each **Not verified** unless a human records live observations. Owner checklist: connect; fine/coarse pan; crowded target and crate→tiny gear cycle; inspect/interact; context/settings navigation/cancel; disconnect/reconnect; focus change; keyboard/mouse/gamepad prompt switching.

Official API references: [Input](https://docs.godotengine.org/en/4.7/classes/class_input.html), [InputMap](https://docs.godotengine.org/en/4.7/classes/class_inputmap.html), [controllers](https://docs.godotengine.org/en/4.7/tutorials/inputs/controllers_gamepads_joysticks.html), [Node](https://docs.godotengine.org/en/4.7/classes/class_node.html), and [macOS export](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html).
