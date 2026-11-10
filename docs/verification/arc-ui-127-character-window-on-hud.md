# ARC-UI-127 — Character window stands on the HUD (no gap above it)

```text
Work-Item: ARC-UI-127
Result: The character sheet stands on the bottom HUD strip at every window size.
Components: Godot character sheet layout, native bar smoke
Human-Gates: none
Requirements: FR-UI-007
```

- AC1. Diagnose in the game, in Legacy and HD, the gap between the character window and the HUD that the 2026-11-10 graphics handoff lists as open.
- AC2. The sheet ends where the bottom strip begins at every window size, centred, at the bars' whole factor, under the top strip.
- AC3. Hit areas, art, pages and the 800 × 600 layout are unchanged. The affected bar smoke and the root checks pass.

## Diagnosis

Native bar smoke with the installed HD pack and without it, window 1280 × 800 (factor 1): the sheet (800 × 400) was placed under the
top strip (`y = TopInset`), the bottom strip is anchored to the window's bottom, so 200 px of the world lay between them; at
1920 × 1080 (factor 1) the gap was 480 px. The gap is the same in Legacy, so it is layout, not HD art or the importer. The HD sheet
matches Legacy in composition, anchors and states at 1x; the only difference is the redraw's greyer wood tone (a matter of taste,
not a defect). No art redraw is needed for this item. In the original's 800 × 600 screen the sheet (41 + 400 = 441) already ends
where the bottom strip begins, so the rule is unchanged there.

## Change

`AreaGameBarPanel.DockedWindowTop(size, factor, heightUnits)`: the window y that ends at the top of the bottom strip, at least the
top strip's end; with the bars hidden the top inset. `AreaCharacterSheetArtView.Place` uses it. The free height now lies between
the top strip and the sheet. Other windows (logbook, inventory, map, shop, saves) are not changed; centring the window between the
strips or a fractional scale of the whole interface are separate owner decisions (the owner deferred them on 2026-11-08).

## Verification

Installed HD pack manifest sha256 `1abf6a51...b4d39` (not changed by this task). The bar smoke now checks, after Character opens the
sheet, that its bottom edge equals the bottom strip's top, its left edge equals the strip's, its scale is the bars' factor and it
lies below the top strip, at the smoke's window size (1280 x 800), at 1920 x 1080 and after returning.

- Before: native bar smoke readbacks (HD and Legacy), gap 200 px at 1280 x 800 and 480 px at 1920 x 1080 above the HUD.
- After: the same readbacks, the sheet ends at the bottom strip's top edge at 1280 x 800, 1600 x 1000 and 1920 x 1080 (Legacy and HD).
- `./arcanum smoke pr --stages bar --main-menu` with `ARC_HD_PACK=<installed pack>` and `ARC_AREA_BAR_HD_VERIFY=1` (ticket 10904, after
  merging origin/main): PASS, including the new assertion. The 3 `Texture ... leaked` lines are the known shutdown diagnostics.
- Root checks (ticket 10885): locked restore, build, filtered tests (all suites 0 failed), the viewer tests (1486 passed), DAT probe
  self-test and original oracle self-test PASS.
- Independent review (fresh Sonnet subagent on the exact diff): no findings; low notes (null art view without game art in the
  smoke, window clamped by the display) do not change the result.

Screenshots stay under `local/` and are original-derived.
