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

Fresh settings and Reset defaults set `invertCameraX=true` and `invertCameraY=true`; therefore right-stick right/down moves the world image right/down (camera left/up). The characterized GPAD001 baseline also applied positive world-image translation for right/down; under the owner amendment `true` deliberately retains that visual sign and `false` reverses the selected axis. The former requirement that false/false preserve the baseline is superseded. Settings exposes independent X/Y toggles and shows the resulting right/down image directions, separate camera/actor deadzones and sensitivities, the retained Nearby Goal comparison mode, recenter, and Reset. Values are versioned in `user://gamepad_lab_settings_v1.json`; missing, corrupt, or unsupported-version data falls back to the inverted v1 defaults.

Direct desired-direction intent is the default prototype policy. Nearby Goal is a small comparison mode: it stores an actor-relative 96 px destination, steers toward that stored point, and refreshes it at most every 0.15 seconds and only beyond 12 px hysteresis. Bounded internal progression makes its open-space displacement independent of render-frame partition while preserving the real target, an explicit `Arrived` stop state, and a visible goal marker. Actor sensitivity is linear prototype effort through displacement: full input at 0.5 is 120 px/s and at 2.0 is 480 px/s from the 240 px/s policy. Neutral, blocked, modal, cancel, focus, and disconnect clear movement; both modes latch a held blocked direction until neutral or material redirection. The procedural courtyard has a wall, corner, narrow passage, actor heading, and goal marker. A bounded 8 px synthetic occupancy sweep prevents a large frame from tunnelling through that fixture and preserves the last safe position. It does not claim pathfinding or general physics. See [../../docs/input/gamepad-movement-study.md](../../docs/input/gamepad-movement-study.md) for the research comparison and limitations.

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

The Core harness characterizes the historical GPAD001 image-right/down baseline and all four owner-confirmed camera combinations; it also covers defaults/reset/serialization/corruption fallback; per-domain deadzones, diagonal normalization, delta and zoom behavior; concurrent stick separation; active device, focus/disconnect/modal safety; actor-relative stored-goal cadence/hysteresis, Direct-vs-Nearby steering and arrival; linear 0.5/2.0 effort, default-policy long-vs-split Nearby Goal displacement, blocked states, and a swept thin-obstacle fixture. The Godot self-test is explicitly `SIMULATION`; it drives the actual adapter/scene and checks the four actual world-image camera signs, responsive reticle/projection, controller arbitration/rendered provenance diagnostics, modal repeats, actor displacement independently from camera displacement, concurrent sticks, settings reload, modal/focus/disconnect clearing, Direct-vs-Nearby steering, long-vs-split sensitivity ratios, and wall/corner/far-side narrow-passage motion.

After the candidate is clean and committed, run:

    experiments/gamepad-lab/tools/validate-macos.sh

It builds the ignored exact-head app at `artifacts/builds/ARC-GPAD-002/<head>/GamepadLab.app`, runs exported smoke plus a three-process settings write/restart/reset/restart check against an isolated per-validation lab file (removed on success or failure), and runs the ordinary capture under a 30-second deadline. It removes that temporary settings file before hashing/inventorying the app bundle and durable gate evidence in `manifest.json`, then reads the manifest back to verify every durable inventory entry and atomically records a relative exact-head manifest pointer plus manifest SHA-256 in `artifacts/builds/ARC-GPAD-002/current.json`. The app is local, unsigned, and unnotarized. `SIMULATION` checks do not establish physical Xbox Bluetooth behavior.

## Owner checklist

Connect an Xbox Bluetooth controller; verify both default inversion axes and each independent toggle; move/pan concurrently; test drift, modal/focus/disconnect/reconnect safety; walk into the wall/corner/narrow passage and use F2 recenter; compare movement modes; then confirm targeting, cycling, keyboard/mouse fallback, and that left-stick movement has no interaction side effect. Physical axes/buttons, disconnect/reconnect, drift, glyph correctness, and comfort are **Not verified** until that session.

Official version-pinned references: [controllers](https://docs.godotengine.org/en/4.7/tutorials/inputs/controllers_gamepads_joysticks.html), [NavigationAgent usage](https://docs.godotengine.org/en/4.7/tutorials/navigation/navigation_using_navigationagents.html), [AStarGrid2D](https://docs.godotengine.org/en/4.7/classes/class_astargrid2d.html), [CharacterBody2D](https://docs.godotengine.org/en/4.7/classes/class_characterbody2d.html).