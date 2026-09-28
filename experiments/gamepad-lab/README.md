# Synthetic Gamepad Input Lab

Standalone Godot 4.7.2 / .NET 8 experiment for semantic gamepad input. It uses procedural primitives only: no original game data, extracted content, third-party assets, or product integration.

## Controls and scope

| Intent | Keyboard | Controller |
|---|---|---|
| Move synthetic actor | WASD | Left stick |
| Pan world image/camera | arrows (world mode) | Right stick |
| Menu navigation | arrows (modal) | D-pad / left stick |
| Confirm / cancel | Enter/Space / Escape | A / B |
| Inspect / context / settings | I / C / F1 | X / Y / Start |
| Recenter actor | F2 | F2 keyboard equivalent only |

The left stick has no interaction, targeting, mouse-emulation, or OS-cursor behavior. It supplies direct desired direction to the visible synthetic actor. The right stick only pans the world image. They can operate concurrently. The center reticle remains a targeting/interacting feature independent of the actor.

Fresh settings and Reset defaults set `invertCameraX=true` and `invertCameraY=true`; therefore right-stick right/down moves the world image right/down (camera left/up). Settings exposes independent X/Y toggles, separate camera/actor deadzones and sensitivities, the retained Nearby Goal comparison mode, recenter, and Reset. Values are versioned in `user://gamepad_lab_settings_v1.json`; missing, corrupt, or unsupported-version data falls back to the inverted v1 defaults.

Direct desired-direction intent is the default prototype policy. Nearby Goal is a small comparison mode: an actor-relative 96 px goal refreshes at most every 0.15 seconds and only beyond 12 px hysteresis. Neutral, blocked, modal, cancel, focus, and disconnect clear movement. The procedural courtyard has a wall, corner, narrow passage, actor heading, and goal marker. Collision blocks the actor and cancels the goal; it does not claim pathfinding. See [../../docs/input/gamepad-movement-study.md](../../docs/input/gamepad-movement-study.md) for the research comparison and limitations.

## Checks

Use only the canonical SDK 8.0.425 and Godot 4.7.2 mono:

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

The Core harness covers defaults/reset/serialization/corruption fallback; all four inversion combinations; per-domain deadzones, diagonal normalization, delta and zoom behavior; concurrent stick separation; active device, focus/disconnect/modal safety; actor-relative goal cadence; stop/blocked states; and no implicit interaction pulse. The Godot self-test is explicitly `SIMULATION`; it drives the actual adapter/scene and checks actor displacement independently from camera displacement, concurrent sticks, settings reload, modal/focus clearing, and wall collision.

After the candidate is clean and committed, run:

    experiments/gamepad-lab/tools/validate-macos.sh

It builds the ignored exact-head app at `artifacts/builds/ARC-GPAD-002/<head>/GamepadLab.app`, runs exported smoke, and atomically records `manifest.json` plus `artifacts/builds/ARC-GPAD-002/current.json`. The app is local, unsigned, and unnotarized. `SIMULATION` checks do not establish physical Xbox Bluetooth behavior.

## Owner checklist

Connect an Xbox Bluetooth controller; verify both default inversion axes and each independent toggle; move/pan concurrently; test drift, modal/focus/disconnect/reconnect safety; walk into the wall/corner/narrow passage and use F2 recenter; compare movement modes; then confirm targeting, cycling, keyboard/mouse fallback, and that left-stick movement has no interaction side effect. Physical axes/buttons, disconnect/reconnect, drift, glyph correctness, and comfort are **Not verified** until that session.

Official version-pinned references: [controllers](https://docs.godotengine.org/en/4.7/tutorials/inputs/controllers_gamepads_joysticks.html), [NavigationAgent usage](https://docs.godotengine.org/en/4.7/tutorials/navigation/navigation_using_navigationagents.html), [AStarGrid2D](https://docs.godotengine.org/en/4.7/classes/class_astargrid2d.html), [CharacterBody2D](https://docs.godotengine.org/en/4.7/classes/class_characterbody2d.html).