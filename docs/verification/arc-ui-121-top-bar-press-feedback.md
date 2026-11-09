# ARC-UI-121 — Quick-click feedback for Fate Points and Sleep

```text
Work-Item: ARC-UI-121
Result: Show the Fate Points and Sleep Down frames on quick clicks.
Components: Godot game bar, native bar smoke
Human-Gates: none
Requirements: FR-UI-002
```

- AC1. Reproduce the missing Down frame on Fate Points and Sleep by sending press and release without a draw between them on the ARC-UI-120 base.
- AC2. In Legacy and HD native bar smoke, the first draw after that click matches the held Down pixels and differs from Hover, then settles to Hover. Record the texture source for both states.
- AC3. In both modes, each button stays Down while held inside and cancels when released outside. Inside release still triggers the existing action and sounds once. Existing button geometry and art selection are preserved.
- AC4. Combat retains its existing native smoke behavior. The affected bar smoke and required root checks pass.

## Implementation

The local base is `187d09f9d`, the merge of [PR #883](https://github.com/Simarglok/arcanum-steamwoven/pull/883). Its tree matches `9765006bd`. Fate Points and Sleep now share the game bar's 80 ms minimum Down display with Combat. The time starts at the first draw that shows Down. Each button tracks its own feedback so a second button can receive a quick click while the first one's feedback is visible. The existing press, release, sound and action paths still run synchronously, with an outside release canceling the action.

Both buttons use the existing two-frame art 137. The frame selector maps Idle and Hover to frame 0 and Down to frame 1. Geometry and art lookup are unchanged. The existing actions still show the window-not-built notices. FR-UI-002 remains partial in the viewer.

## Verification on macOS (arm64)

The smoke sends press and release synchronously through the viewport, then reads the next rendered frame. The candidate compares pixels with Hover and held Down, checks the return to Hover, drags a held press outside, and verifies the exact sequence of press and release sounds. The action trace logs two invocations per button, one for each inside release. No viewport images or game assets are stored.

| Run-queue ticket | Result |
| --- | --- |
| 10132 | Legacy reproduction on `187d09f9d` with the baseline probe. Both actions fired, and both buttons drew frame 0 after release. The Down assertion failed as expected. |
| 10135 | Legacy bar smoke passed. Both buttons drew frame 1 after the quick click, matching held Down, then returned to Hover. Both canceled outside and used Legacy textures. Combat feedback also passed. |
| 10136 | HD bar smoke passed with the same behavior. The displayed Idle, Hover and Down textures were HD on both buttons. Combat and the opt-in HD top bar checks also passed. |

Legacy used `./arcanum smoke pr --stages bar --main-menu`. HD reused the same viewer build with `--no-build`, `ARC_AREA_BAR_HD_VERIFY=1` and `ARC_HD_PACK` set to the local `interface-redraw-4x` pack. The tickets identify logs under the project support root's `run-logs` folder. All three runs logged the same Godot texture leak at shutdown. The two candidate runs exited successfully.

The probe checks the actual TextureRect texture for each state. A missing HD frame can fall back to Legacy through the existing interface texture lookup. There was no fallback in the three sampled states of either button with this pack.

- Root locked restore, build and filtered synthetic tests passed (10130, 4,513 passed and 48 skipped by the suites). The game bar viewer is outside the root solution and was built separately by the smoke.
- DAT probe self-test passed.
- Original oracle self-test passed all 29 tests on native macOS using the existing locked Unicorn 2.1.4 environment.
