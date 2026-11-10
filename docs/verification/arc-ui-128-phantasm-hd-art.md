# ARC-UI-128 — Phantasm button: bright top edge removed (local art)

```text
Work-Item: ARC-UI-128
Result: Record the local repair of the three Phantasm frames (art 80) and its installation.
Components: local HD pack (not in git), native character-window smoke
Human-Gates: none recorded in the local report
Requirements: FR-PRODUCT-004
```

Local label before renumbering: ARC-UI-117 (that number belongs to [PR #888](https://github.com/Simarglok/arcanum-steamwoven/pull/888)).
Local sources: `local/res095/character_repair/arc-ui-117`. No original or derived art is in git.

## Defect and repair

The owner's screenshot was gone, so the defect was reproduced on the pack with ARC-UI-116 installed from the three PNGs of art 80
and the `char_spells` backing. A light straight edge sat in rows 0–1 of the frames themselves, above the gold rim.
Row 0 is now transparent. Row 1 takes the colour of the neighbouring rim row with at most 25 % opacity, limited by the source
coverage of both rows. Rows 2–95, the 96×96 size and the anchor metadata are unchanged. The three states are compared at one
scale in `phantasm-before-after.png` and `diagnosis.png` (local).

## Verification (macOS, 2026-11-09)

- Native smoke on main `f968fba06` with isolated settings and the candidate pack (queue 10090) passed: the character window
  resolved 311/311 frames as HD, and the sheet and magic-page checks passed. The first run (10086) rejected frame 80 because the
  manifest still held the old PNG sizes; the local builder then refreshed the sizes and now compares every file with the manifest.
- All 1,144 files of the pack were compared: three PNGs and the manifest differ; 1,139 entries and the recipe are byte-identical,
  ARC-UI-115/116 included.
- No tracked code changed, so no root checks or PR were needed for the local PNGs.

## Installation (2026-11-09 11:22)

Game closed, original pack and `BUILD.txt` matched the snapshot, atomic swap, then the installed pack and the backup were
re-verified (receipt `installed.json` lists the backup path). Manifest SHA-256 before:
`0fcb407d502a55d3e56ad1bf5d918e137440e022759e9c03ab5a8a903ad1270f`; after:
`eb62bff7a12c29a9d8d9ba58acb6e9cb271298c40e5b9704e98dcbd016ee1c6d`. See [the chain](hd-interface-local-repairs.md).
