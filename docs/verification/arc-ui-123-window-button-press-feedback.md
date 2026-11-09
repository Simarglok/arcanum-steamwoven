# ARC-UI-123 — Quick-click feedback for game bar window buttons

```text
Work-Item: ARC-UI-123
Result: Show Down on quick clicks of Character, Inventory, Logbook and Schematics.
Components: Godot game bar, native bar smoke
Human-Gates: none
Requirements: FR-UI-002
```

- AC1. On the ARC-UI-121 base, reproduce the missing Down frame for the four buttons with back-to-back press and release in Legacy and HD.
- AC2. In both modes, the first draw after a quick inside release uses the held Down frame and texture, then returns to Hover, for opening and closing each window.
- AC3. An inside release still opens or closes immediately, a held press does not activate the button, and releasing outside cancels it. The window buttons retain their art, hit areas and sounds.
- AC4. The affected bar smoke and the required root checks pass.

## Implementation

The four window buttons use the existing game bar feedback from ARC-UI-121. A successful short click keeps the Down frame for at least 80 ms from its first draw. The window opens or closes on release through the existing action path. The button keeps its art, position, hit area and sounds.

The scope covers Character, Inventory, Logbook and Schematics. Opening the world map hides the game bar, and Skills and Spells use toggle behavior. FR-UI-002 remains partial in the viewer.

## Verification on macOS (arm64)

The baseline probe on `f0262db9` sent a press and release without an intervening draw. In both Legacy (10145) and HD (10146), all four buttons drew Hover (frame 2) on the first frame after release. Holding the same buttons drew Down (frame 1). The windows opened immediately, the panel stayed visible, and outside release canceled. The HD probe observed HD textures on all four buttons.

The candidate smoke repeats the back-to-back input to open and close every window. It checks immediate action, the actual frame and texture on the first draw, the return to Hover, a held press, outside release, and the exact sequence of press and release sounds. In the HD run, the Down texture must be HD.

Legacy uses `./arcanum smoke pr --stages bar --main-menu`. HD reuses the viewer build with `--no-build`, `ARC_AREA_BAR_HD_VERIFY=1` and the local `interface-redraw-4x` pack selected by `ARC_HD_PACK`. Run tickets refer to logs in the project support root's `run-logs` folder. No viewport images or game assets are stored.

- DAT probe self-test passed.
- Original oracle self-test passed all 29 tests with the existing locked Unicorn 2.1.4 environment.
- Root locked restore, build and filtered synthetic tests passed (10152, 4,513 passed and 48 skipped).
- The first viewer build (10150) stopped inside Roslyn with a `MissingMethodException`. The repeat build (10155) passed.
- The preliminary Legacy (10157) and HD (10159) smokes passed with the provisional code ARC-UI-122 in the log. The independent review requested a strict HD texture assertion, which is included in the final smokes.
- Final Legacy (10163) and HD (10164) smokes passed with the strict texture assertion. All four buttons drew Down after quick open and quick close, settled to Hover, and canceled outside.

The baseline and candidate logs include the same Godot texture leak at shutdown. The candidate smokes exited successfully.
