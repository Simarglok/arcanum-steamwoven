# ARC-UI-132 — Character window fills the height between the HUD strips

```text
Work-Item: ARC-UI-132
Result: The HD character window fills the whole height between the top and bottom HUD strips at its own aspect ratio.
Components: Godot character sheet layout, native bar smoke
Human-Gates: none
Requirements: FR-UI-007
```

- AC1. Find why the HD window does not fill the space between the strips.
- AC2. With the HD pack the sheet fills the free height between the strips at 2:1, centred (side margins only at 16:9); the HD art keeps its quality.
- AC3. Without the HD pack the original's bitmaps stay on a whole scale. Hit areas, art, pages and the 800 × 400 layout are unchanged.

## Cause

The HD sheet (`refinement02/char_maint`, 3200 × 1600) is exactly 4× the original window (800 × 400): its aspect ratio is right. The window was
placed at the bars' whole factor (`floor(min(W / 800, H / 600))`), which is the factor of the whole 800 × 600 screen, not of the window.
At 1920 × 1080 that is 1, so the 800 × 400 sheet lay on a free height of 880 px (1080 − 41 − 159) and used 400 of it.

## Change

`AreaGameBarPanel.FittedWindow` returns the scale and position: `min(free height / 400, width / 800)`, never below the bars' factor, standing on the
bottom strip. With the HD pack on, the scale is exact (a 4× texture is sampled with mipmapped linear filtering through its canvas texture, so a
fractional scale keeps its quality); without it the largest whole scale. At 1280 × 800: 1.5 (HD) or 1; at 1920 × 1080: 2.2 (HD) or 2;
at 800 × 600: 1. The sheet panel places it again every frame while it is open, so the HD toggle and a resize are covered. Pages, spell view and hit areas are children and follow the scale.

## Verification

Installed HD pack manifest sha256 `478fbb92...eeba7` (not changed by this task). The bar smoke asserts, with the sheet open and at 1280 × 800, 1920 × 1080 and back:
the bottom edge on the bottom strip, centred, uniform scale, in HD the scale equals free height / 400, without HD a whole scale that is the largest that fits.
`ARC_AREA_SHEET_CAPTURE=<dir>` saves a readback per window size (original-derived, stays under `local/`).

- `./arcanum smoke pr --stages bar --main-menu`: Legacy PASS; with `ARC_HD_PACK=<installed pack>` and `ARC_AREA_BAR_HD_VERIFY=1`: HD PASS
  (the 3 `Texture ... leaked` lines are the known shutdown diagnostics).
- Readbacks: HD at 1920 × 1080 — window 1760 × 880 from the top strip to the bottom strip, 80 px side margins, text and frames sharp;
  HD at 1280 × 800 — 1200 × 600; Legacy at 1920 × 1080 — 1600 × 800 (2×).

## Options considered

| Option | Result |
|---|---|
| Fractional scale by free height, HD only (chosen) | Fills the height, aspect kept, no art change |
| Whole scale only | At 1080p 2× leaves 80 px, at 1280 × 800 stays 1× (200 px empty) |
| Non-uniform stretch | Distorts the art and the hit areas; rejected |
| Redrawn wider art (Codex) | Needed only to use the side margins; not asked for |

- Root checks (tickets 11145 and 11149-11162): locked restore, build, filtered tests (all suites 0 failed, the viewer tests with `TestCategory!=RealData`),
  DAT probe and original oracle self-tests PASS; `./arcanum smoke self-test` PASS. HD and Legacy bar smoke PASS after the review fixes.
- Independent review (fresh Sonnet subagent on the exact diff): no maths bug. Fixed: the HD expectation is capped by the width too, the strict 0.5 px
  tolerance, a null readback guard, and the redundant `InterfaceHd.Changed` hook (the panel places the sheet every frame while it is open) was removed.
  Accepted: the portrait and the bitmap text are sampled nearest at a fractional scale (the readbacks show them sharp).
