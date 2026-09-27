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

InputMap declares the keyboard and SDL semantic actions (including both left-stick directions); the adapter maintains per-device event state and accepts joy events only from the deterministic active device. With no active device, a frame collects meaningful controller candidates and selects the lowest ID; additional connected controllers remain diagnostic-only. It samples held stick/keyboard pan every frame, so panning is continuous and frame-rate/zoom scaled. `Camera2D` is intentionally outside the translated WorldRoot, so the rendered courtyard actually moves under the fixed reticle. Active-controller disconnect and focus transitions clear held/repeat state; reconnect/focus/reset require a complete neutral sampled frame and a following arm frame. Circular deadzone defaults to 0.20 (0.05–0.45); sensitivity defaults to 1.0 (0.5–2.0). The app labels engine delivery `LIVE OS EVENT (not physical usability proof)` and all injected automation `SIMULATION`.

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
    /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . -- --self-test

The Core executable harness covers radial deadzone/no drift, normalization and delta/zoom scaling, same-frame lowest-device arbitration/quarantine and method thresholds, every rising-edge pulse, focus/reset stale-edge suppression, modal world suppression, deterministic digital/analog menu repeat, epsilon ties/exact hysteresis boundary, cycling/lock invalidation/removal/out-of-radius, offscreen/world-ground fallback, and projection fixtures at 1280×800 and 1920×1080. Godot self-test drives actual adapter key/mouse events through the scene, checks manual target-or-ground selection, rendered canvas displacement from continuous pan, settings/menu activation, focus and disconnect hooks with explicit **SIMULATION** provenance.

## Evidence limits and owner checklist

Export/capture proves a local unsigned/unnotarized macOS prototype, not distribution readiness. Bluetooth axes/buttons, disconnect/reconnect on a real Xbox controller, drift, glyph correctness, and comfort are **Not verified** unless a human records live observations. Owner checklist: connect; fine/coarse pan; crowded target and crate→tiny gear cycle; inspect/interact; context/settings navigation/cancel; disconnect/reconnect; focus change; keyboard/mouse/gamepad prompt switching.

Official API references: [Input](https://docs.godotengine.org/en/4.7/classes/class_input.html), [InputMap](https://docs.godotengine.org/en/4.7/classes/class_inputmap.html), [controllers](https://docs.godotengine.org/en/4.7/tutorials/inputs/controllers_gamepads_joysticks.html), [Node](https://docs.godotengine.org/en/4.7/classes/class_node.html), and [macOS export](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html).
