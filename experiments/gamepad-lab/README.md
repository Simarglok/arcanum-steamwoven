# Synthetic Gamepad Input Lab

Standalone Godot 4.7.2 / .NET 8 experiment for semantic gamepad input. It uses procedural primitives only: no original game data, extracted content, third-party assets, or product integration.

## Controls and scope

ARC-GPAD-005 adds **Contextual (initial), QuickSlots and Radial** to this one lab, not three scenes/routers. The complete binding tables, context priority, all-ten slot assignment/use, user-copy editor, v2 preferences, safe switching/reset and full owner comparison checklist are canonical in [gamepad-presets](../../docs/input/gamepad-presets.md). The table below summarizes the retained keyboard fallback; controller faces are preset-dependent.

| Intent | Keyboard | Controller |
|---|---|---|
| Move synthetic actor | WASD | Left stick |
| Pan world image/camera / enter Manual | arrows (world mode) | Right stick |
| Menu navigation | arrows (modal) | D-pad / left stick |
| Confirm / cancel current modal or pending layer | Enter/Space / Escape | A / B (overrides world slots) |
| Inspect / context / settings | I / C / F1 | Preset face bindings / Menu |
| Previous / next eligible target | Q / E | D-pad Left / Right |
| Highlights / camera zoom | Keyboard fallback retained | RB held / LT out, RT in |
| Camera-only recenter / target lock / hub | Settings recenter | L3 / R3 / View |
| Recenter actor and camera / enter Follow | F2 | F2 keyboard equivalent only |

The left stick has no interaction, targeting, mouse-emulation, or OS-cursor behavior. It supplies direct desired direction to the visible synthetic actor. The camera starts in **Follow** and centers the actor subject to synthetic-map bounds. A post-deadzone right-stick (or arrow-key) pan has priority, enters **Manual**, and does not change actor intent; both domains can therefore operate concurrently.

Manual does not snap back when the right stick is released while actor movement remains held, nor when both domains first become neutral. A full post-deadzone neutral sample arms recenter. Only a subsequent actor-only neutral-to-active edge returns to Follow and directly centers the current actor; a simultaneous or later right-stick edge instead stays Manual and clears stale eligibility. This direct, clamped transition cannot overshoot and can be interrupted immediately by pan. Circular per-domain deadzones are the drift hysteresis. “Movement stopped” means released/neutral input, not a collision-blocked actor: held blocked actor input remains a Follow command, while Manual still requires the neutral sequence.

The procedural map is deliberately bounded and camera clamping accounts for viewport size and zoom; if a map is smaller than the viewport, the only legal center is the map midpoint. These are synthetic bounds, not an assertion about original Arcanum camera bounds. The centered HUD reticle and semantic target eligibility stay viewport-based and independent of the actor. In Follow the actor's movement changes which world objects are within that reticle field; it does not turn movement into targeting.

Fresh settings and Reset defaults set `invertCameraX=true` and `invertCameraY=true`; therefore right-stick right/down moves the world image right/down (camera left/up). The characterized GPAD001 baseline also applied positive world-image translation for right/down; under the owner amendment `true` deliberately retains that visual sign and `false` reverses the selected axis. Settings exposes independent X/Y toggles, camera/actor deadzones and sensitivities, Direct Intent/Nearby Goal, preset/copy controls and UI 100/150/200%. Values now use lab-only `user://gamepad_lab_settings_v2.json`; valid v1 is migrated only when v2 is absent and v1 is never modified. Corrupt/unknown v2 falls back diagnostically without overwriting it or reading stale v1. Preset switching and Settings Reset preserve positions, camera mode and zoom; F2 is the separate actor/camera reset. WORLD preferences/defaults are unchanged.

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

The Core harness characterizes the historical GPAD001 image-right/down baseline and all four owner-confirmed camera combinations; it covers the Follow/Manual transition table (priority, right-release-with-left-held, neutral arm, new actor-only resume, interrupt, safety reset, blocked-held input, map edges, small world, and zoom-aware bounds), defaults/reset/serialization/corruption fallback; per-domain deadzones, diagonal normalization, delta and zoom behavior; concurrent stick separation; active device, focus/disconnect/modal safety; actor-relative stored-goal cadence/hysteresis, Direct-vs-Nearby steering and arrival; linear 0.5/2.0 effort, default-policy long-vs-split Nearby Goal displacement, blocked states, and a swept thin-obstacle fixture. The Godot self-test is explicitly `SIMULATION`; it drives the actual adapter/scene and checks the four actual world-image camera signs, Follow/Manual displacement and status, responsive reticle/projection, controller arbitration/rendered provenance diagnostics, modal repeats, actor displacement independently from camera displacement, settings reload, modal/focus/disconnect clearing, Direct-vs-Nearby steering, long-vs-split sensitivity ratios, and wall/corner/far-side narrow-passage motion.

After the candidate is clean and committed, configure its ignored artifact root with `tools/arcanum-local configure add-artifact-root --id ID PATH` in an owned home. For the default legacy ARC-GPAD-003 endpoint, run:

    ARCANUM_LOCAL_ROOT_ID=ID experiments/gamepad-lab/tools/validate-macos.sh

It re-execs beneath the exact candidate lease and performs a measured free-space preflight before building the ignored exact-head app at `artifacts/builds/ARC-GPAD-003/<head>/GamepadLab.app`. The bound v2 preflight record contains the full candidate identity, output/temporary/reserve budget and observed free space. It runs exported smoke plus a three-process settings write/restart/reset/restart check against an isolated per-validation lab file (removed on success or failure), and retains a bounded automated `--capture` result under a 30-second deadline. Capture is not ordinary-launch evidence: ordinary launch remains explicitly `NOT_VERIFIED`. It removes that temporary settings file before creating the nonempty digest-and-byte-bound v2 evidence inventory in `manifest.json`; only after all complete gates, including deep/strict codesign, pass does it register lifecycle `exported` and atomically record the v2 exact-head pointer. Failed validation never registers or changes the pointer. The app is local, unsigned, and unnotarized. `SIMULATION` checks do not establish physical Xbox Bluetooth behavior.

The endpoint requires effective `TMPDIR`. Output budget uses a verified accepted/current payload or an explicit single-payload `ARCANUM_OUTPUT_BYTES`/`ARCANUM_ESTIMATE_MANIFEST`, not directory-history `du`. Export/temp/build/cache devices are checked independently. Validation exits without a manifest; the endpoint freezes sampled capacity and isolated temporary-allocation evidence before manifest-only finalization. Godot/.NET cache attribution is incomplete, so production temporary usage is explicitly unavailable (`null`), not the budget estimate or a measured zero. Finalized/ambiguous partial same-head candidates reject intact. See [local workflow](../../docs/workflow/local-development.md).

## ARC-GPAD-005 export and verification

Use `ARCANUM_GAMEPAD_EXPORT_TASK=ARC-GPAD-005`, `ARCANUM_LOCAL_ROOT_ID=gpad005`, explicit owned `ARCANUM_LOCAL_HOME` and `TMPDIR` with this **same** endpoint. Configure a separate ignored `artifacts/gamepad-builds` root; it writes `ARC-GPAD-005/<full-head>/GamepadLab.app`, never the older 003 package. The endpoint requires the complete clean committed tree and runs Core `--all`, canonical verbose SDK/editor bridge, fresh source **and exported** non-headless 54-tuple matrices/native baselines, isolated source/export preferences restart/migration, shared input/WORLD headless+graphical/root regressions and lifecycle-policy tests before export inventory/seals/codesign/registration. Reports bind actual source/build/runtime/argv/exit and their original assertion paths. An old dirty matrix is not stable-head evidence. Offscreen tuples of the same production scene prove logical GUI/raster, not physical monitor readability. Registration and the later ordinary no-test/no-injection one-window launch/postruntime-integrity gates are distinct from owner physical validation. [Exact recipe and selection](../../docs/workflow/local-development.md#gamepad-presets-artifact-arc-gpad-005).

## Owner checklist

For the three-template candidate use the **complete [preset comparison checklist](../../docs/input/gamepad-presets.md#owner-comparison-checklist)**, including slots 1/4/5/8/9/10, self/ally/enemy/ground, cancel, turn end/party/modal resume, customization/restart/reset and time-to-action/wrong-slot-target/accidental-use/movement-stop/comfort comparisons. The following older movement checklist remains supplementary.

On 2026-09-29 the owner said the controller movement/camera scheme matched the
request. This is a bounded human observation, not verification of Bluetooth
reconnect, drift, all axes/settings, exhaustive stability, or original parity.

Connect an Xbox Bluetooth controller; verify both default inversion axes and each independent toggle; move/pan concurrently; test drift, modal/focus/disconnect/reconnect safety; walk into the wall/corner/narrow passage and use F2 recenter; compare movement modes; then confirm targeting, cycling, keyboard/mouse fallback, and that left-stick movement has no interaction side effect. Physical axes/buttons, disconnect/reconnect, drift, glyph correctness, and comfort are **Not verified** until that session.

Official version-pinned references: [controllers](https://docs.godotengine.org/en/4.7/tutorials/inputs/controllers_gamepads_joysticks.html), [NavigationAgent usage](https://docs.godotengine.org/en/4.7/tutorials/navigation/navigation_using_navigationagents.html), [AStarGrid2D](https://docs.godotengine.org/en/4.7/classes/class_astargrid2d.html), [CharacterBody2D](https://docs.godotengine.org/en/4.7/classes/class_characterbody2d.html).