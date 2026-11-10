# ARC-UI-120 — Combat button feedback on a quick click

```text
Work-Item: ARC-UI-120
Result: Show the Combat button's Down frame on a quick mouse click in the game bar.
Components: Godot game bar, native bar smoke
Human-Gates: none
Requirements: FR-UI-002
```

- AC1. In a native Legacy and HD bar smoke, back-to-back mouse press and release inside Combat with no draw between events display the Down frame on the next draw and settle to Hover.
- AC2. In both smoke modes, holding Combat displays Down without activating it. Releasing inside toggles combat once per click. Releasing outside clears the hold and leaves combat unchanged.
- AC3. The change uses the existing Combat art and hit area, and preserves keyboard bindings and the combat command path.
- AC4. The affected bar smoke and the required root checks pass.

## Implementation

The game bar used to assign Down on press and Hover on release. Both assignments could happen before Godot presented a frame. The panel now keeps Combat's Down art for at least 80 ms measured from the first draw, if a successful release comes sooner. A held press continues to follow pointer enter and exit. An outside release cancels the press. The action and its sound still run immediately on an inside release through the existing bar effect.

The change is confined to presentation. The hit rectangle, asset lookup, bindings and combat rules use the existing paths. FR-UI-002 remains partial in the viewer, so the coverage summary counts stay the same.

## Verification on macOS (arm64)

The automated bar smoke injects both events synchronously through the viewport and observes the next draw. In each interface mode it compares the viewport pixels from that draw to held Down and to Hover. It also checks immediate activation, the return to Hover, a held press and release, and cancellation after dragging outside. No viewport image is stored.

| Run-queue ticket | Result |
| --- | --- |
| 10119 | Reproduction on `f968fba06` with the regression probe. The first post-release draw showed frame 2 (Hover) after the combat toggle. The smoke failed as expected. |
| 10121 | Legacy bar smoke passed. Quick Down matched held Down. Two inside releases produced two successful combat changes, on then off. The outside release produced none. |
| 10125 | HD bar smoke passed the same cases with HD textures from the installed `interface-redraw-4x` pack. The opt-in HD top bar checks also passed. |

Legacy used `./arcanum smoke pr --stages bar --main-menu`. HD used the same stage with `--no-build`, `ARC_AREA_BAR_HD_VERIFY=1` and `ARC_HD_PACK` pointing to the installed pack. Logs are in the run queue's `run-logs` directory under the project support root. Each of the three native runs logged a pre-existing Godot texture leak during shutdown. The two candidate runs exited successfully.

- `python3 -B tools/research/dat_probe.py --self-test` passed.
- `python3 -B tools/original-oracle/oracle.py --self-test` passed 29 tests after provisioning the pinned Unicorn dependency in the checkout.
- Root locked restore and build passed. The first synthetic test run (10124) failed in `The_process_runner_reads_both_streams_and_stops_a_tool_on_cancel` at the assertion comparing the two lists of process output. The root solution inputs match the base commit.
- The full synthetic test rerun passed (10127). The GOG installer test failure did not recur.
